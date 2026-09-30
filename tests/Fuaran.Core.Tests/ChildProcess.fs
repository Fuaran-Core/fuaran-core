module Fuaran.Core.Tests.ChildProcess

open System.Diagnostics
open System.Text

// ---------------------------------------------------------------------------
// Spawning a child whose stdout the cross-host suites compare byte-for-byte.
//
// `Process.StandardOutput` decodes the pipe with the PARENT's
// `Console.OutputEncoding` unless told otherwise. On Windows that is the
// console's code page, so under an OEM page (CP850) the child's UTF-8 `é`
// (C3 A9) decodes to `├⌐` and every vector carrying a non-ASCII character
// diverges from the in-process interpreter leg.
//
// That made the green gate a function of the console the runner happened to
// inherit rather than of the code: the Phase 316 and Phase 698 sweeps passed
// under `dotnet run` and failed when the built test dll was invoked directly.
//
// Both children the suite spawns — `node` and `dotnet fsi` — write UTF-8 to a
// redirected stream, so the decode is pinned here instead of left ambient.
// Every redirected spawn goes through this one function deliberately: a site
// that omits the setting still passes on the machine that wrote it, so the
// omission is invisible exactly where it would be caught.
// ---------------------------------------------------------------------------

let private utf8 = UTF8Encoding false

/// A `ProcessStartInfo` with both output streams redirected and their decoding
/// pinned to UTF-8, independent of the console's code page.
let redirected (fileName: string) (arguments: string) =
    let psi = ProcessStartInfo(fileName, arguments)
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.UseShellExecute <- false
    psi.StandardOutputEncoding <- utf8
    psi.StandardErrorEncoding <- utf8
    psi

/// `git <arguments>` run in `workingDir` through `redirected`: its standard output, or why git
/// could not say — a non-zero exit (with what git wrote), or git not runnable at all. The ONE git
/// helper the surface and corpus tests share (Phase 299): the public-surface and wire-surface
/// baselines and the sibling-corpus resolver each carried a copy of this body until then.
let git (workingDir: string) (arguments: string) : Result<string, string> =
    try
        let psi = redirected "git" arguments
        psi.WorkingDirectory <- workingDir
        use p = Process.Start psi
        let out = p.StandardOutput.ReadToEnd()
        let err = p.StandardError.ReadToEnd()
        p.WaitForExit()

        if p.ExitCode <> 0 then
            Error(sprintf "`git %s` exited %d: %s" arguments p.ExitCode ((err + out).Trim()))
        else
            Ok out
    with e ->
        Error(sprintf "`git %s` could not be run: %s" arguments e.Message)
