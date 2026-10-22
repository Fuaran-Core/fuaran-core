module Fuaran.Core.Tests.RegistryProbeTests

// ---------------------------------------------------------------------------
// Phase 561 — the post-push registry probe's CLASSIFICATION, held over recorded answers.
//
// `.github/scripts/probe-registry.ps1` turns two inputs into a verdict per package id: what the
// push step said about it, and what nuget.org's flat container answers. On `v0.40.1` (publish run
// 39640035761) every push landed and indexing outran the fifteen-minute budget, so the job went red
// over a release that had shipped. The probe now keeps the answers apart:
//
//   served                    the index lists the version;
//   NOT PUSHED                no push line accepted the id or found it present (red at once, before
//                             the registry is asked anything; a re-run is the remedy);
//   PUSHED, INDEXING PENDING  pushed, and the index does not list it yet (a 200 without the version,
//                             or a 404) when the backed-off budget is spent (waiting is the remedy);
//   NOT SERVED                the same index answer on a hand run, which has no push record;
//   COULD NOT ASK             the request failed (a 5xx here): no answer about the id at all.
//
// The script runs as the workflow runs it, against a local stand-in for the flat container that
// answers each id with a RECORDED response: `registry-probe/index-served.json` is nuget.org's index
// for `fuaran.core.ops` as served on 2026-10-22 (it lists 0.40.1 and 0.40.2, not 0.40.3), and
// `index-not-found.xml` is its 404 body for an id it has never seen (byte-order mark dropped). The
// push records are the `v0.40.1` publish job's own push-step output, run timestamps stripped:
// attempt 1 (`push-0.40.1-accepted.log`, every package `Your package was pushed.`) and attempt 2
// (`push-0.40.1-already-present.log`, every package `already exists` under `--skip-duplicate`).
// A 5xx cannot be recorded on demand, so it is the one synthetic answer.
// ---------------------------------------------------------------------------

open System
open System.IO
open System.Net
open System.Net.Sockets
open System.Text
open System.Text.RegularExpressions
open System.Threading
open Expecto

/// One answer the stand-in registry gives for an id's `index.json`: a status, the body and its media
/// type, as nuget.org labels them (a body with no text media type reaches the probe as bytes).
type private Answer =
    { Status: int
      ContentType: string
      Body: string }

let private fixture (name: string) =
    Path.Combine(Repo.root (), "tests", "Fuaran.Core.Tests", "registry-probe", name)

let private served () =
    { Status = 200
      ContentType = "application/json"
      Body = File.ReadAllText(fixture "index-served.json") }

let private neverSeen () =
    { Status = 404
      ContentType = "application/xml"
      Body = File.ReadAllText(fixture "index-not-found.xml") }

let private failing =
    { Status = 503
      ContentType = "text/plain"
      Body = "Service Unavailable" }

/// A local flat container on a free loopback port, answering `/v3-flatcontainer/<id>/index.json`
/// from `answers` (keyed by the lowercased id; an id it was not given is never seen) and counting
/// the requests it served.
type private Registry(answers: Map<string, Answer>) =
    let port =
        let probe = new TcpListener(IPAddress.Loopback, 0)
        probe.Start()
        let p = (probe.LocalEndpoint :?> IPEndPoint).Port
        probe.Stop()
        p

    let listener = new HttpListener()
    let mutable asked = 0

    do
        listener.Prefixes.Add(sprintf "http://localhost:%d/" port)
        listener.Start()

    let serve () =
        try
            while listener.IsListening do
                let context = listener.GetContext()
                Interlocked.Increment(&asked) |> ignore

                let segments =
                    context.Request.Url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)

                let answer =
                    match segments with
                    | [| "v3-flatcontainer"; id; "index.json" |] ->
                        answers |> Map.tryFind id |> Option.defaultWith neverSeen
                    | _ -> neverSeen ()

                let bytes = Encoding.UTF8.GetBytes answer.Body
                context.Response.StatusCode <- answer.Status
                context.Response.ContentType <- answer.ContentType
                context.Response.ContentLength64 <- int64 bytes.Length
                context.Response.OutputStream.Write(bytes, 0, bytes.Length)
                context.Response.Close()
        with
        | :? HttpListenerException
        | :? ObjectDisposedException
        | :? InvalidOperationException -> ()

    let worker = Thread(serve, IsBackground = true)
    do worker.Start()

    member _.Address = sprintf "http://localhost:%d/v3-flatcontainer" port
    member _.Asked = Volatile.Read(&asked)

    interface IDisposable with
        member _.Dispose() =
            listener.Stop()
            listener.Close()

/// A synthetic repository whose packable roster is exactly `ids`, for the probe's `-Root`.
let private withRoster (ids: string list) (f: string -> unit) =
    let dir =
        Path.Combine(
            Path.GetTempPath(),
            sprintf "registry-probe-%d-%s" Environment.ProcessId (Guid.NewGuid().ToString "N")
        )

    try
        for id in ids do
            let project = Path.Combine(dir, "src", id, id + ".fsproj")
            Directory.CreateDirectory(Path.GetDirectoryName project) |> ignore

            File.WriteAllText(
                project,
                "<Project><PropertyGroup><IsPackable>true</IsPackable></PropertyGroup></Project>"
            )

        f dir
    finally
        if Directory.Exists dir then
            Directory.Delete(dir, true)

/// The probe's exit code and its output (both pipes, colour codes removed), run the way the publish
/// workflow runs it.
let private probe (root: string) (version: string) (registry: Registry) (extra: string list) : int * string =
    let script = Path.Combine(Repo.root (), ".github", "scripts", "probe-registry.ps1")

    match
        ChildProcess.argv
            "pwsh"
            ([ "-NoProfile"
               "-NonInteractive"
               "-File"
               script
               "-Root"
               root
               "-Version"
               version
               "-Registry"
               registry.Address ]
             @ extra)
        |> ChildProcess.withEnv "NO_COLOR" "1"
        |> ChildProcess.run
    with
    | Error why -> failtestf "the probe could not be run: %s" why
    | Ok o -> o.Exit, Regex.Replace(o.Stdout + o.Stderr, "\x1b\[[0-9;]*m", "")

/// The ids a verdict line names: the line beginning `==== registry: <verdict>`, read after its first
/// `: `, up to a following sentence, each id without any parenthesised reason. Empty when no line
/// carries that verdict.
let private named (verdict: string) (output: string) : Set<string> =
    output.Split('\n')
    |> Array.map _.Trim()
    |> Array.tryFind (fun l -> l.StartsWith("==== registry: " + verdict, StringComparison.Ordinal))
    |> Option.map (fun line ->
        let rest = line.Substring(("==== registry: " + verdict).Length)
        let ids = rest.Substring(rest.IndexOf(": ", StringComparison.Ordinal) + 2)

        let ids =
            match ids.IndexOf(". ", StringComparison.Ordinal) with
            | -1 -> ids
            | i -> ids.Substring(0, i)

        ids.Split(", ")
        |> Array.map (fun s -> Regex.Replace(s, @"\s*\(.*\)$", "").Trim())
        |> Set.ofArray)
    |> Option.defaultValue Set.empty

let private once = [ "-Attempts"; "1"; "-IntervalSeconds"; "0" ]

let private pushLog (name: string) = [ "-PushLog"; fixture name ]

/// Three ids from the `v0.40.1` push records, so both recorded logs cover them.
let private ops, text, cli =
    "Fuaran.Core.Ops", "Fuaran.Core.Text", "Fuaran.Core.Idl.Cli"

let private lower (id: string) = id.ToLowerInvariant()

[<Tests>]
let tests =
    testSequenced
    <| testList
        "RegistryProbe"
        [ test "a hand run classifies recorded index answers: served, absent, never seen, 5xx" {
              withRoster [ ops; text; cli ] (fun root ->
                  use registry = new Registry(Map [ lower ops, served (); lower cli, failing ])

                  // `text` is answered 404, the recorded never-seen body.
                  let exit, output = probe root "0.40.1" registry once
                  Expect.equal exit 1 "an id not served fails the probe"
                  Expect.equal (named "NOT SERVED" output) (set [ text ]) "a 404 is an answer: not served"
                  Expect.equal (named "COULD NOT ASK" output) (set [ cli ]) "a 503 is no answer: could not ask"
                  Expect.isFalse (output.Contains "PUSHED, INDEXING PENDING") "a hand run has no push record to claim"

                  let exit, output = probe root "0.40.3" registry once
                  Expect.equal exit 1 "the recorded index does not list 0.40.3"

                  Expect.equal
                      (named "NOT SERVED" output)
                      (set [ ops; text ])
                      "a 200 whose version list lacks the version is an answer: not served")
          }

          test "every id pushed and served reports green, with the backed-off budget printed first" {
              withRoster [ ops; text; cli ] (fun root ->
                  use registry =
                      new Registry(Map [ for id in [ ops; text; cli ] -> lower id, served () ])

                  // The default schedule, which never sleeps here: every id is served on the first pass.
                  let exit, output = probe root "0.40.1" registry (pushLog "push-0.40.1-accepted.log")
                  Expect.equal exit 0 (sprintf "a release whose pushes landed and is served is green:\n%s" output)

                  Expect.stringContains output "the push record" "the probe says what it read from the push step"

                  Expect.stringContains
                      output
                      "3 accepted, 0 already present, 0 not pushed"
                      "the recorded attempt-1 lines are acceptances"

                  // 30 + 60 + 120 + 240 + 300 x 10 = 3450 s: the next 300 s wait would pass the 60-minute budget.
                  Expect.stringContains
                      output
                      "up to 15 pass(es) over 57.5 min (waits 30s, 60s, 120s, 240s, 300s, 300s, 300s, 300s, 300s, 300s, 300s, 300s, 300s, 300s; budget 60 min)"
                      "the backed-off budget is bounded and stated before the first pass"

                  Expect.stringContains output "all 3 ids are served at 0.40.1" "the verdict")
          }

          test "pushed and not indexed is INDEXING PENDING, citing the push lines; a 5xx stays COULD NOT ASK" {
              withRoster [ ops; text; cli ] (fun root ->
                  use registry = new Registry(Map [ lower cli, failing ])

                  for log, cited in
                      [ "push-0.40.1-accepted.log", "Your package was pushed."
                        "push-0.40.1-already-present.log", "already exists at feed" ] do
                      let exit, output =
                          probe root "0.40.1" registry (pushLog log @ [ "-IntervalSeconds"; "0" ])

                      Expect.equal exit 1 (sprintf "%s: the budget ran out with ids pending" log)

                      Expect.equal
                          (named "PUSHED, INDEXING PENDING" output)
                          (set [ ops; text ])
                          (sprintf "%s: pushed and answered absent is indexing pending" log)

                      Expect.equal
                          (named "COULD NOT ASK" output)
                          (set [ cli ])
                          (sprintf "%s: a 503 is still no answer" log)

                      Expect.equal
                          (named "NOT SERVED" output)
                          Set.empty
                          (sprintf "%s: never the hand run's verdict" log)

                      Expect.equal (named "NOT PUSHED" output) Set.empty (sprintf "%s: every id was pushed" log)

                      Expect.stringContains
                          output
                          "nothing needs re-pushing"
                          (sprintf "%s: says waiting is the remedy" log)

                      for id in [ ops; text ] do
                          Expect.isTrue
                              (output.Split('\n')
                               |> Array.exists (fun l -> l.Contains(id + ": ") && l.Contains cited))
                              (sprintf "%s: %s's pending entry cites the push line %A" log id cited))
          }

          test "an id with no push line is NOT PUSHED, before the registry is asked anything" {
              withRoster [ ops; text ] (fun root ->
                  use registry = new Registry(Map [ lower ops, served (); lower text, served () ])

                  let recorded = File.ReadAllLines(fixture "push-0.40.1-accepted.log")

                  let dir =
                      Path.Combine(Path.GetTempPath(), sprintf "registry-probe-log-%s" (Guid.NewGuid().ToString "N"))

                  Directory.CreateDirectory dir |> ignore

                  try
                      // The attempt-1 record's first eight lines: `ops` pushed, then its symbols.
                      let headOnly = Path.Combine(dir, "head.log")
                      File.WriteAllLines(headOnly, recorded |> Array.take 8)
                      let exit, output = probe root "0.40.1" registry [ "-PushLog"; headOnly ]
                      Expect.equal exit 1 "an id never pushed fails"
                      Expect.equal (named "NOT PUSHED" output) (set [ text ]) "the id with no push line"
                      Expect.equal registry.Asked 0 "no wait can serve an unpushed id, so nothing is asked"

                      // The same lines without the package's own outcome: the symbol package's
                      // `Your package was pushed.` that follows is not the package's.
                      let symbolsOnly = Path.Combine(dir, "symbols-only.log")
                      File.WriteAllLines(symbolsOnly, recorded |> Array.take 8 |> Array.removeAt 3)
                      let exit, output = probe root "0.40.1" registry [ "-PushLog"; symbolsOnly ]
                      Expect.equal exit 1 "a symbol package's acceptance is not the package's"
                      Expect.equal (named "NOT PUSHED" output) (set [ ops; text ]) "neither package was accepted"

                      let exit, output =
                          probe root "0.40.1" registry [ "-PushLog"; Path.Combine(dir, "absent.log") ]

                      Expect.equal exit 1 "a push record that does not exist fails"
                      Expect.stringContains output "there is no push record to read" "and says why"
                      Expect.equal registry.Asked 0 "still nothing asked"
                  finally
                      Directory.Delete(dir, true))
          } ]
