namespace Fuaran.Core

// ============================================================================
//  Vector<'T> (Phase 417) — the opaque, immutable typed vector a column's storage
//  sits behind. A sealed class over an array, an offset and a length on .NET; the
//  same class under Fable, where the array is a typed array when the compiler
//  knows the element type and a plain one otherwise, and nothing here depends on
//  which — except that a vector of booleans is PACKED, a byte a row in a
//  `Uint8ClampedArray`, and read back as booleans (Phase 431, DECISIONS.md D145).
//  The type is the ownership contract (Phase 418): no public member writes,
//  construction copies (`ofArray`) or takes ownership (`adopt`), a `slice` is a
//  view, and the backing array is reached only through `Vector.Unsafe.borrow`.
// ============================================================================

/// The element identity every vector compares and hashes by (Phase 417): a float by the column
/// layer's float order — every NaN one value, `-0.0` equal to `0.0`, the identity `Cell.compare`
/// and `Cell.token` already give a cell — and every other element by structural equality. The
/// float arm is chosen by a type test on the boxed element rather than on the type parameter,
/// because that reads the same under Fable, where a generic parameter is erased and an array
/// built generically may be a plain array rather than a typed one.
module internal VectorElements =

    /// The hash of a float under the identity above: one value for every NaN, one for both zeroes.
    let hashFloat (f: float) : int =
        if System.Double.IsNaN f then 0x7FC00000
        elif f = 0.0 then 0
        else hash f

    /// Element equality: floats by `Cell.compareFloat`, everything else structurally.
    let equal (a: 'T) (b: 'T) : bool =
        match box a, box b with
        | (:? float as x), (:? float as y) -> Cell.compareFloat x y = 0
        | _ -> Unchecked.equals a b

    /// Element hash, agreeing with `equal`.
    let hashOf (a: 'T) : int =
        match box a with
        | :? float as x -> hashFloat x
        | _ -> Unchecked.hash a

    /// How many leading elements a hash reads — the bound FSharp.Core's structural hash of a list
    /// or an array also keeps, so hashing a long vector is cheap and equal vectors still hash equal.
    [<Literal>]
    let HashedPrefix = 18

#if FABLE_COMPILER
/// The packed backing of a vector of booleans under Fable (Phase 431, DECISIONS.md D145): a
/// `Uint8ClampedArray` holding `1` for `true` and `0` for `false`, a byte a row where a plain
/// JavaScript array of booleans holds eight. No F# element type compiles to a `Uint8ClampedArray`
/// (a `byte[]` is a `Uint8Array`), so the class of a backing says whether it is packed.
///
/// Every access here is a raw index, never fable-library's bounds-checked `item` / `setItem`: those
/// are ONE function shared by every array read in a program, so what kinds of array they have seen
/// decides how fast every read through them runs — a float fold at 1,000 rows measured between 0.5
/// and 15 microseconds by nothing but the history of that helper (D145.5). Every `Vector` read under
/// Fable therefore indexes its backing through `raw` or `get`, packed or not. Callers read inside a
/// range they have already checked: a vector's range lies within its backing by construction.
module internal PackedBools =

    /// Is `items` a packed backing?
    [<Fable.Core.Emit("$0 instanceof Uint8ClampedArray")>]
    let isPacked (items: obj) : bool = Fable.Core.Util.jsNative

    /// The element at `j` of a mask backing as a boolean, whether it is packed (`1` / `0`) or plain
    /// (`true` / `false`): JavaScript truthiness reads both.
    [<Fable.Core.Emit("!!$0[$1]")>]
    let get (items: obj) (j: int) : bool = Fable.Core.Util.jsNative

    [<Fable.Core.Emit("$0[$1]")>]
    let private at (xs: obj) (j: int) : obj = Fable.Core.Util.jsNative

    [<Fable.Core.Emit("$0 === true")>]
    let private isTrue (x: obj) : bool = Fable.Core.Util.jsNative

    [<Fable.Core.Emit("$0 === false")>]
    let private isFalse (x: obj) : bool = Fable.Core.Util.jsNative

    [<Fable.Core.Emit("$0[$1] = 1")>]
    let private setOne (packed: obj) (j: int) : unit = Fable.Core.Util.jsNative

    [<Fable.Core.Emit("new Uint8ClampedArray($0)")>]
    let private create (n: int) : 'T[] = Fable.Core.Util.jsNative

    /// A packed copy of `xs` when it holds at least one element and every one is a JavaScript
    /// boolean, else `null` — one pass, and the first element decides for any other array in one
    /// read, so the test costs every other array nothing. It stores the number `1` for `true` and
    /// leaves the zero the new array holds for `false`: storing a boolean into a clamped array
    /// converts it on a slow path, measured at five times the cost (DECISIONS.md D145.5).
    let tryPack (xs: 'T[]) : 'T[] =
        let n = xs.Length

        if n = 0 || not (isTrue (at (box xs) 0) || isFalse (at (box xs) 0)) then
            null
        else
            let packed = create n
            let mutable all = true
            let mutable j = 0

            while all && j < n do
                let x = at (box xs) j

                if isTrue x then
                    setOne (box packed) j
                    j <- j + 1
                elif isFalse x then
                    j <- j + 1
                else
                    all <- false

            if all then packed else null

    /// The element at `j` of any backing, read directly.
    [<Fable.Core.Emit("$0[$1]")>]
    let raw (items: 'T[]) (j: int) : 'T = Fable.Core.Util.jsNative

    /// The element at `j` of a backing, a packed one read back as a boolean.
    let inline read (packed: bool) (items: 'T[]) (j: int) : 'T =
        if packed then
            unbox<'T> (box (get (box items) j))
        else
            raw items j
#endif

/// An opaque, immutable vector of `'T` (Phase 417): the storage behind every column, which two
/// holders of one column share. Reading is an indexer, `Length` and the `Vector` module; nothing
/// public writes, and a vector's contents never change after construction except through a
/// `Vector.Unsafe.borrow` the borrower has promised not to write through (Phase 418).
///
/// Equality and hashing are by element under `VectorElements`' identity — a float vector holding a
/// NaN equals another holding a NaN at the same index, and `-0.0` equals `0.0` — so two columns
/// compare the way their cells do, on every host. Vectors are not ordered: a column is not sorted
/// by its contents, and a record holding one is not comparable either.
[<Sealed>]
type Vector<'T> internal (items: 'T[], offset: int, length: int) =
#if FABLE_COMPILER
    let packed = PackedBools.isPacked (box items)
#endif

    /// The backing array, shared with every view over it; never written through this handle.
    member internal _.Items: 'T[] = items

    /// Where this vector's first element sits in `Items`.
    member internal _.Offset: int = offset

    /// The number of elements.
    member _.Length: int = length

#if FABLE_COMPILER
    /// Is the backing a packed array of booleans (Phase 431)? Fable only.
    member internal _.Packed: bool = packed
#endif

    /// The element at `i`. Bounds-checked against THIS vector's length on both hosts, so a view
    /// never reads past its end into its parent's storage and an out-of-range read under Fable
    /// raises rather than answering `undefined`; the exception is `IndexOutOfRangeException`, as
    /// an array's would be. `Vector.tryItem` is the total read.
    member _.Item
        with get (i: int): 'T =
            if i < 0 || i >= length then
                raise (System.IndexOutOfRangeException())

#if FABLE_COMPILER
            PackedBools.read packed items (offset + i)
#else
            items[offset + i]
#endif

    /// Element-wise equality under the float-aware identity (`VectorElements.equal`), over the
    /// view's own range only.
    override this.Equals(other: obj) : bool =
        match other with
        | :? Vector<'T> as that ->
            if length <> that.Length then
                false
            else
                let mutable same = true
                let mutable i = 0

                while same && i < length do
#if FABLE_COMPILER
                    same <-
                        VectorElements.equal
                            (PackedBools.read packed items (offset + i))
                            (PackedBools.read that.Packed that.Items (that.Offset + i))
#else
                    same <- VectorElements.equal items[offset + i] that.Items[that.Offset + i]
#endif
                    i <- i + 1

                same
        | _ -> false

    /// The length combined with the first `VectorElements.HashedPrefix` elements, by the same
    /// identity `Equals` compares under.
    override this.GetHashCode() : int =
        let mutable h = length
        let n = min length VectorElements.HashedPrefix

        for i in 0 .. n - 1 do
#if FABLE_COMPILER
            h <- (h * 31) + VectorElements.hashOf (PackedBools.read packed items (offset + i))
#else
            h <- (h * 31) + VectorElements.hashOf items[offset + i]
#endif

        h

/// Reads and constructors over `Vector<'T>`. Every constructor either COPIES what it is handed
/// (`ofArray`, `ofList`, `ofSeq`, `init`, `map`) or TAKES OWNERSHIP of a fresh array (`adopt`);
/// nothing here writes into a vector that exists.
module Vector =

    /// The empty vector.
    let empty<'T> : Vector<'T> = Vector<'T>(Array.empty, 0, 0)

    /// The number of elements.
    let length (v: Vector<'T>) : int = v.Length

    /// Is the vector empty?
    let isEmpty (v: Vector<'T>) : bool = v.Length = 0

    /// The element at `i` — the indexer as a function; raises on an out-of-range index.
    let item (i: int) (v: Vector<'T>) : 'T = v[i]

    /// The element at `i`, or `None` for an out-of-range index — the total read.
    let tryItem (i: int) (v: Vector<'T>) : 'T option =
        if i < 0 || i >= v.Length then None else Some v[i]

    /// A vector over ITS OWN COPY of `xs`: a later write into `xs` does not reach it. Under Fable a
    /// copy of booleans is packed, a byte a row (Phase 431).
    let ofArray (xs: 'T[]) : Vector<'T> =
#if FABLE_COMPILER
        let packed = PackedBools.tryPack xs
        Vector<'T>((if isNull packed then Array.copy xs else packed), 0, xs.Length)
#else
        Vector<'T>(Array.copy xs, 0, xs.Length)
#endif

    /// A vector that TAKES OWNERSHIP of `xs` without copying (Phase 418). The caller promises not
    /// to read or write `xs` again: from this call the array is the vector's, and a write through
    /// the caller's reference would change a value every holder of the vector sees. The zero-copy
    /// route for an array the caller has just built and will not keep. A broken promise here is
    /// invisible to the type; `Conformance.columnOwnershipLaws` is the check that names it. Under
    /// Fable an array of booleans is PACKED, a byte a row (Phase 431, DECISIONS.md D145.4): the one
    /// case where `adopt` copies, after which the caller's array is simply unused. An array already
    /// packed is taken as it is.
    let adopt (xs: 'T[]) : Vector<'T> =
#if FABLE_COMPILER
        let packed = PackedBools.tryPack xs
        Vector<'T>((if isNull packed then xs else packed), 0, xs.Length)
#else
        Vector<'T>(xs, 0, xs.Length)
#endif

    /// A vector of the list's elements, in order.
    let ofList (xs: 'T list) : Vector<'T> = adopt (List.toArray xs)

    /// A vector of the sequence's elements, in order; the sequence is read once.
    let ofSeq (xs: seq<'T>) : Vector<'T> = adopt (Array.ofSeq xs)

    /// `n` elements, the `i`-th being `f i`.
    let init (n: int) (f: int -> 'T) : Vector<'T> = adopt (Array.init n f)

    // Under Fable each read below tests `Packed` ONCE, outside its loop (Phase 431): a vector that is
    // not packed runs the loop it ran before, and a packed one a loop of its own, so neither loop's
    // element load is made polymorphic by the other (measured, DECISIONS.md D145.5). On .NET the
    // `#else` arm is the code as it was.

    /// A fresh array holding the elements — a copy, never the storage; under Fable a plain array of
    /// booleans for a packed vector (Phase 431).
    let toArray (v: Vector<'T>) : 'T[] =
#if FABLE_COMPILER
        if v.Packed then
            Array.init v.Length (fun i -> PackedBools.read true v.Items (v.Offset + i))
        else
            Array.sub v.Items v.Offset v.Length
#else
        Array.sub v.Items v.Offset v.Length
#endif

    /// The elements as a list, in order.
    let toList (v: Vector<'T>) : 'T list =
        let mutable acc = []
#if FABLE_COMPILER
        if v.Packed then
            for i in v.Length - 1 .. -1 .. 0 do
                acc <- PackedBools.read true v.Items (v.Offset + i) :: acc
        else
            for i in v.Length - 1 .. -1 .. 0 do
                acc <- (PackedBools.raw v.Items (v.Offset + i)) :: acc
#else
        for i in v.Length - 1 .. -1 .. 0 do
            acc <- v.Items[v.Offset + i] :: acc
#endif
        acc

    /// `f` on each element, in order.
    let iter (f: 'T -> unit) (v: Vector<'T>) : unit =
#if FABLE_COMPILER
        if v.Packed then
            for i in 0 .. v.Length - 1 do
                f (PackedBools.read true v.Items (v.Offset + i))
        else
            for i in 0 .. v.Length - 1 do
                f (PackedBools.raw v.Items (v.Offset + i))
#else
        for i in 0 .. v.Length - 1 do
            f v.Items[v.Offset + i]
#endif

    /// `f` on each index and element, in order.
    let iteri (f: int -> 'T -> unit) (v: Vector<'T>) : unit =
#if FABLE_COMPILER
        if v.Packed then
            for i in 0 .. v.Length - 1 do
                f i (PackedBools.read true v.Items (v.Offset + i))
        else
            for i in 0 .. v.Length - 1 do
                f i (PackedBools.raw v.Items (v.Offset + i))
#else
        for i in 0 .. v.Length - 1 do
            f i v.Items[v.Offset + i]
#endif

    /// A left fold over the elements, in order.
    let fold (f: 'State -> 'T -> 'State) (state: 'State) (v: Vector<'T>) : 'State =
        let mutable acc = state
#if FABLE_COMPILER
        if v.Packed then
            for i in 0 .. v.Length - 1 do
                acc <- f acc (PackedBools.read true v.Items (v.Offset + i))
        else
            for i in 0 .. v.Length - 1 do
                acc <- f acc (PackedBools.raw v.Items (v.Offset + i))
#else
        for i in 0 .. v.Length - 1 do
            acc <- f acc v.Items[v.Offset + i]
#endif
        acc

    /// A NEW vector of `f` over each element; the source is unchanged.
    let map (f: 'T -> 'U) (v: Vector<'T>) : Vector<'U> =
        let out = Array.zeroCreate v.Length
#if FABLE_COMPILER
        if v.Packed then
            for i in 0 .. v.Length - 1 do
                out[i] <- f (PackedBools.read true v.Items (v.Offset + i))
        else
            for i in 0 .. v.Length - 1 do
                out[i] <- f (PackedBools.raw v.Items (v.Offset + i))
#else
        for i in 0 .. v.Length - 1 do
            out[i] <- f v.Items[v.Offset + i]
#endif
        adopt out

    /// A NEW vector of `f` over each index and element; the source is unchanged.
    let mapi (f: int -> 'T -> 'U) (v: Vector<'T>) : Vector<'U> =
        let out = Array.zeroCreate v.Length
#if FABLE_COMPILER
        if v.Packed then
            for i in 0 .. v.Length - 1 do
                out[i] <- f i (PackedBools.read true v.Items (v.Offset + i))
        else
            for i in 0 .. v.Length - 1 do
                out[i] <- f i (PackedBools.raw v.Items (v.Offset + i))
#else
        for i in 0 .. v.Length - 1 do
            out[i] <- f i v.Items[v.Offset + i]
#endif
        adopt out

    /// Does some element satisfy `pred`?
    let exists (pred: 'T -> bool) (v: Vector<'T>) : bool =
        let mutable found = false
        let mutable i = 0
#if FABLE_COMPILER
        if v.Packed then
            while not found && i < v.Length do
                found <- pred (PackedBools.read true v.Items (v.Offset + i))
                i <- i + 1
        else
            while not found && i < v.Length do
                found <- pred (PackedBools.raw v.Items (v.Offset + i))
                i <- i + 1
#else
        while not found && i < v.Length do
            found <- pred v.Items[v.Offset + i]
            i <- i + 1
#endif
        found

    /// The index of the first element satisfying `pred`, or `None`.
    let tryFindIndex (pred: 'T -> bool) (v: Vector<'T>) : int option =
        let mutable found = None
        let mutable i = 0
#if FABLE_COMPILER
        if v.Packed then
            while found.IsNone && i < v.Length do
                if pred (PackedBools.read true v.Items (v.Offset + i)) then
                    found <- Some i

                i <- i + 1
        else
            while found.IsNone && i < v.Length do
                if pred (PackedBools.raw v.Items (v.Offset + i)) then
                    found <- Some i

                i <- i + 1
#else
        while found.IsNone && i < v.Length do
            if pred v.Items[v.Offset + i] then
                found <- Some i

            i <- i + 1
#endif
        found

    /// A ZERO-COPY view of `count` elements from `start`: the same storage, a new range (Phase 418).
    /// Raises `ArgumentOutOfRangeException` where the range does not lie within the vector, as
    /// `Array.sub` would.
    let slice (start: int) (count: int) (v: Vector<'T>) : Vector<'T> =
        if start < 0 || count < 0 || start + count > v.Length then
            raise (System.ArgumentOutOfRangeException("count", "the slice does not lie within the vector"))

        Vector<'T>(v.Items, v.Offset + start, count)

    /// The one route to a vector's backing array (Phase 418), for interop that needs a raw typed
    /// array — a chart library wanting a `Float64Array`, a native call. The name is the warning.
    module Unsafe =

        /// A lent backing array with the range the vector occupies in it: the elements are
        /// `Array[Offset .. Offset + Length - 1]`, and the array may hold other vectors' elements
        /// outside that range.
        [<Struct>]
        type Borrowed<'T> =
            {
                /// The backing array itself — shared, and NEVER written: a write here changes the value
                /// every holder of the vector, and of every other view over the array, reads. Under
                /// Fable a bool vector's is its packed `Uint8ClampedArray` (Phase 431): read an element
                /// as a condition (`if a[i] then`), never as a value, which is the number `1` or `0`.
                Array: 'T[]
                /// The index of the vector's first element in `Array`.
                Offset: int
                /// The number of elements the vector occupies from `Offset`.
                Length: int
            }

        /// Lend the backing array. The borrower promises not to write through it: the vector is
        /// immutable by contract, not by copy, and a write here is visible to everyone holding it —
        /// past the vector's own range too, into a neighbouring view of the same array. A consumer
        /// that borrows proves its pipeline keeps the promise with `Conformance.columnOwnershipLaws`,
        /// which fingerprints every column before and after and names the one whose bytes moved.
        /// Under Fable a vector of booleans lends its PACKED backing, a `Uint8ClampedArray` of `1`
        /// and `0`, with no copy (Phase 431, DECISIONS.md D145.1): typed `bool[]`, it reads right as
        /// a condition and as a number used as a value; a write of `true` or `false` stores `1` or `0`.
        let borrow (v: Vector<'T>) : Borrowed<'T> =
            { Array = v.Items
              Offset = v.Offset
              Length = v.Length }
