// Test-assembly stub of the Fuaran-UI tier's host prelude (`Fuaran.UI.HostPrelude`).
//
// The IDL's `THosted` slots (Accessibility.role, StateBehaviour.onError's
// arg) name host types + codecs that live in the CONSUMING tier, compiled ahead of its
// generated module. This test assembly compiles the same generated code (`UiGenerated.fs`,
// the drift-guarded snapshot), so it carries an identical stub under the same module path.
// Keep byte-for-byte in sync with fuaran-dotnet `src/Fuaran.UI/HostPrelude.fs` — the corpus
// pins the wire bytes on both sides, so a semantic drift fails a byte gate, not silently.
module Fuaran.UI.HostPrelude

open Fuaran.Core

/// Wire-adjacent error taxonomy for `StateBehaviour.onError` payloads.
type ErrorKind =
    | NotFound
    | Forbidden
    | Server
    | Network
    | Timeout
    | BindingResolution

/// The payload handed to a `StateBehaviour.OnError` fallback closure.
type ErrorPayload =
    { Kind: ErrorKind
      Message: string
      CorrelationId: string }

/// Opaque handle to a selected file's blob (`Id` is the only wire-visible part;
/// `Handle` carries the boxed browser `File` on browser hosts).
type FileRef = { Id: string; Handle: obj option }

/// Browser file metadata handed to `FileUpload.onSelect` (closure arg — never
/// serialises, so no codec).
type FileSelection =
    { Name: string
      Size: int64
      MimeType: string
      Ref: FileRef }

/// What a single cell of grid data is, after a column's `Value` projection runs
/// (closure interior — never serialises, so no codec). Pre-formatted strings
/// break numeric sort; use `Numeric` + a `CellFormat` instead.
[<RequireQualifiedAccess>]
type CellValue =
    | Numeric of float
    | Text of string
    | Bool of bool
    | Date of System.DateTimeOffset
    | Empty

/// ARIA role — a closed convenience list plus `Custom` verbatim passthrough
/// (the wire position admits any string; canonical cases emit lower-case).
[<RequireQualifiedAccess>]
type AriaRole =
    | Button
    | Link
    | Dialog
    | Alert
    | Status
    | Banner
    | Navigation
    | Main
    | Form
    | Region
    | Heading
    | Progressbar
    | Tab
    | Tablist
    | Tabpanel
    | Custom of role: string

let encAriaRole (r: AriaRole) : JVal =
    JStr(
        match r with
        | AriaRole.Button -> "button"
        | AriaRole.Link -> "link"
        | AriaRole.Dialog -> "dialog"
        | AriaRole.Alert -> "alert"
        | AriaRole.Status -> "status"
        | AriaRole.Banner -> "banner"
        | AriaRole.Navigation -> "navigation"
        | AriaRole.Main -> "main"
        | AriaRole.Form -> "form"
        | AriaRole.Region -> "region"
        | AriaRole.Heading -> "heading"
        | AriaRole.Progressbar -> "progressbar"
        | AriaRole.Tab -> "tab"
        | AriaRole.Tablist -> "tablist"
        | AriaRole.Tabpanel -> "tabpanel"
        | AriaRole.Custom raw -> raw
    )

let decAriaRole (j: JVal) : Result<AriaRole, string> =
    match j with
    | JStr "button" -> Ok AriaRole.Button
    | JStr "link" -> Ok AriaRole.Link
    | JStr "dialog" -> Ok AriaRole.Dialog
    | JStr "alert" -> Ok AriaRole.Alert
    | JStr "status" -> Ok AriaRole.Status
    | JStr "banner" -> Ok AriaRole.Banner
    | JStr "navigation" -> Ok AriaRole.Navigation
    | JStr "main" -> Ok AriaRole.Main
    | JStr "form" -> Ok AriaRole.Form
    | JStr "region" -> Ok AriaRole.Region
    | JStr "heading" -> Ok AriaRole.Heading
    | JStr "progressbar" -> Ok AriaRole.Progressbar
    | JStr "tab" -> Ok AriaRole.Tab
    | JStr "tablist" -> Ok AriaRole.Tablist
    | JStr "tabpanel" -> Ok AriaRole.Tabpanel
    | JStr other -> Ok(AriaRole.Custom other)
    | _ -> Error "expected JSON string for aria role"
