// Test-assembly stub of the Fuaran-UI tier's host prelude (`Fuaran.UI.HostPrelude`).
//
// The IDL's `THosted` slots (Accessibility.role / .liveRegion, StateBehaviour.onError's
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

/// `aria-live` politeness — closed, lower-case on the wire.
[<RequireQualifiedAccess>]
type LiveRegionKind =
    | Polite
    | Assertive
    | Off

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

let encLiveRegionKind (k: LiveRegionKind) : JVal =
    JStr(
        match k with
        | LiveRegionKind.Polite -> "polite"
        | LiveRegionKind.Assertive -> "assertive"
        | LiveRegionKind.Off -> "off"
    )

let decLiveRegionKind (j: JVal) : Result<LiveRegionKind, string> =
    match j with
    | JStr "polite" -> Ok LiveRegionKind.Polite
    | JStr "assertive" -> Ok LiveRegionKind.Assertive
    | JStr "off" -> Ok LiveRegionKind.Off
    | JStr other -> Error("unknown liveRegion '" + other + "' (expected polite | assertive | off)")
    | _ -> Error "expected JSON string for liveRegion"
