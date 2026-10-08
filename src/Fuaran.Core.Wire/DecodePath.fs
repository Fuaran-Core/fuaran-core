namespace Fuaran.Core

// ============================================================================
//  Phase 310 — the typed decode layer: a refusal is a CODE and a PATH, not a sentence.
//
//  Until this phase `Decode` was six strict combinators returning `Result<_, string>`: no optional
//  or defaulted member, no reader for half the `JVal` kinds, no path, no tag dispatch, no
//  accumulation. Every codec on the spine and every consumer around it wrote the missing half for
//  itself, and none of them could tell a caller WHERE in a document a refusal was or WHAT KIND of
//  refusal it was without scraping the sentence. The layer below is that half, once: a closed code
//  set, a path of keys and indices, combinators that grow the path as a refusal leaves them, and a
//  sentence beside both — the SAME sentence the string forms returned, so a codec moved onto the
//  layer reads identically to every existing caller (DECISIONS.md D99).
// ============================================================================

/// One step of a decode path (Phase 310): a member of an object by its key, or an item of an array
/// by its zero-based index. A path is the list of steps from the document's root, root first.
[<RequireQualifiedAccess>]
type PathSegment =
    /// A member of an object by its exact key; where a document repeats the key, the first.
    | Key of string
    /// An item of an array, zero-based.
    | Index of int

/// Paths into a document (Phase 310): rendered, resolved, and carried on the wire.
[<RequireQualifiedAccess>]
module DecodePath =

    /// `$` for the root, `[i]` for an item, `["key"]` for a member (the key under `Json.escape`) —
    /// the spelling `Json.firstNonFinite` and `Json.firstIllFormedString` already report in.
    let render (path: PathSegment list) : string =
        let sb = System.Text.StringBuilder("$")

        for step in path do
            match step with
            | PathSegment.Index i -> sb.Append('[').Append(string i).Append(']') |> ignore
            | PathSegment.Key k -> sb.Append("[\"").Append(Json.escape k).Append("\"]") |> ignore

        sb.ToString()

    /// One step into a value — the FIRST member of the key where a foreign document repeats one, as
    /// every combinator reads it.
    let private step (s: PathSegment) (v: JVal) : JVal option =
        match s, v with
        | PathSegment.Key k, JObj fields -> fields |> List.tryFind (fun (n, _) -> n = k) |> Option.map snd
        | PathSegment.Index i, JArr items when i >= 0 -> List.tryItem i items
        | _ -> None

    /// The value at `path` in `doc`, or `None` where a step names nothing there.
    let resolve (path: PathSegment list) (doc: JVal) : JVal option =
        let rec go (p: PathSegment list) (v: JVal) =
            match p with
            | [] -> Some v
            | s :: rest -> step s v |> Option.bind (go rest)

        go path doc

    /// A path as a JSON array — a key as a string, an index as an integer. The form a conformance
    /// vector carries, so no host parses a rendered path back.
    let toJson (path: PathSegment list) : JVal =
        path
        |> List.map (function
            | PathSegment.Key k -> JStr k
            | PathSegment.Index i -> JInt i)
        |> JArr

    /// The inverse of `toJson`; `None` for anything but an array of strings and integers.
    let ofJson (v: JVal) : PathSegment list option =
        match v with
        | JArr items ->
            let steps =
                items
                |> List.map (function
                    | JStr k -> Some(PathSegment.Key k)
                    | JInt i -> Some(PathSegment.Index i)
                    | _ -> None)

            if List.forall Option.isSome steps then
                Some(List.choose id steps)
            else
                None
        | _ -> None
