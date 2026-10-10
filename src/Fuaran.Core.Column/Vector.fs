namespace Fuaran.Core

// ============================================================================
//  Vector<'T> (Phase 417) — the opaque, immutable typed vector a column's storage
//  sits behind. A sealed class over an array, an offset and a length on .NET; the
//  same class under Fable, where the array is a typed array when the compiler
//  knows the element type and a plain one otherwise, and nothing here depends on
//  which. The type is the ownership contract (Phase 418): no public member writes,
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

    /// The backing array, shared with every view over it; never written through this handle.
    member internal _.Items: 'T[] = items

    /// Where this vector's first element sits in `Items`.
    member internal _.Offset: int = offset

    /// The number of elements.
    member _.Length: int = length

    /// The element at `i`. Bounds-checked against THIS vector's length on both hosts, so a view
    /// never reads past its end into its parent's storage and an out-of-range read under Fable
    /// raises rather than answering `undefined`; the exception is `IndexOutOfRangeException`, as
    /// an array's would be. `Vector.tryItem` is the total read.
    member _.Item
        with get (i: int): 'T =
            if i < 0 || i >= length then
                raise (System.IndexOutOfRangeException())

            items[offset + i]

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
                    same <- VectorElements.equal items[offset + i] that.Items[that.Offset + i]
                    i <- i + 1

                same
        | _ -> false

    /// The length combined with the first `VectorElements.HashedPrefix` elements, by the same
    /// identity `Equals` compares under.
    override this.GetHashCode() : int =
        let mutable h = length
        let n = min length VectorElements.HashedPrefix

        for i in 0 .. n - 1 do
            h <- (h * 31) + VectorElements.hashOf items[offset + i]

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

    /// A vector over ITS OWN COPY of `xs`: a later write into `xs` does not reach it.
    let ofArray (xs: 'T[]) : Vector<'T> = Vector<'T>(Array.copy xs, 0, xs.Length)

    /// A vector that TAKES OWNERSHIP of `xs` without copying (Phase 418). The caller promises not
    /// to read or write `xs` again: from this call the array is the vector's, and a write through
    /// the caller's reference would change a value every holder of the vector sees. The zero-copy
    /// route for an array the caller has just built and will not keep.
    let adopt (xs: 'T[]) : Vector<'T> = Vector<'T>(xs, 0, xs.Length)

    /// A vector of the list's elements, in order.
    let ofList (xs: 'T list) : Vector<'T> = adopt (List.toArray xs)

    /// A vector of the sequence's elements, in order; the sequence is read once.
    let ofSeq (xs: seq<'T>) : Vector<'T> = adopt (Array.ofSeq xs)

    /// `n` elements, the `i`-th being `f i`.
    let init (n: int) (f: int -> 'T) : Vector<'T> = adopt (Array.init n f)

    /// A fresh array holding the elements — a copy, never the storage.
    let toArray (v: Vector<'T>) : 'T[] = Array.sub v.Items v.Offset v.Length

    /// The elements as a list, in order.
    let toList (v: Vector<'T>) : 'T list =
        let mutable acc = []

        for i in v.Length - 1 .. -1 .. 0 do
            acc <- v.Items[v.Offset + i] :: acc

        acc

    /// `f` on each element, in order.
    let iter (f: 'T -> unit) (v: Vector<'T>) : unit =
        for i in 0 .. v.Length - 1 do
            f v.Items[v.Offset + i]

    /// `f` on each index and element, in order.
    let iteri (f: int -> 'T -> unit) (v: Vector<'T>) : unit =
        for i in 0 .. v.Length - 1 do
            f i v.Items[v.Offset + i]

    /// A left fold over the elements, in order.
    let fold (f: 'State -> 'T -> 'State) (state: 'State) (v: Vector<'T>) : 'State =
        let mutable acc = state

        for i in 0 .. v.Length - 1 do
            acc <- f acc v.Items[v.Offset + i]

        acc

    /// A NEW vector of `f` over each element; the source is unchanged.
    let map (f: 'T -> 'U) (v: Vector<'T>) : Vector<'U> =
        let out = Array.zeroCreate v.Length

        for i in 0 .. v.Length - 1 do
            out[i] <- f v.Items[v.Offset + i]

        adopt out

    /// A NEW vector of `f` over each index and element; the source is unchanged.
    let mapi (f: int -> 'T -> 'U) (v: Vector<'T>) : Vector<'U> =
        let out = Array.zeroCreate v.Length

        for i in 0 .. v.Length - 1 do
            out[i] <- f i v.Items[v.Offset + i]

        adopt out

    /// Does some element satisfy `pred`?
    let exists (pred: 'T -> bool) (v: Vector<'T>) : bool =
        let mutable found = false
        let mutable i = 0

        while not found && i < v.Length do
            found <- pred v.Items[v.Offset + i]
            i <- i + 1

        found

    /// The index of the first element satisfying `pred`, or `None`.
    let tryFindIndex (pred: 'T -> bool) (v: Vector<'T>) : int option =
        let mutable found = None
        let mutable i = 0

        while found.IsNone && i < v.Length do
            if pred v.Items[v.Offset + i] then
                found <- Some i

            i <- i + 1

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
                /// every holder of the vector, and of every other view over the array, reads.
                Array: 'T[]
                /// The index of the vector's first element in `Array`.
                Offset: int
                /// The number of elements the vector occupies from `Offset`.
                Length: int
            }

        /// Lend the backing array. The borrower promises not to write through it: the vector is
        /// immutable by contract, not by copy, and a write here is visible to everyone holding it.
        let borrow (v: Vector<'T>) : Borrowed<'T> =
            { Array = v.Items
              Offset = v.Offset
              Length = v.Length }
