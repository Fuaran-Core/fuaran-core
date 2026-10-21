/// `fuaran-core-oracle` — the extracted models as a process (docs/oracle-protocol.md). With no
/// argument it serves the protocol: a request per line on stdin, a response per line on stdout, in
/// order, until stdin closes. `--version` prints the oracle and protocol versions.
module Fuaran.Core.Oracle.Cli.Program

open System
open System.IO
open System.Text
open System.Threading

let private strictUtf8 = UTF8Encoding(false, true)

/// Serves until end of input. Lines are split on LF over the raw bytes and each is decoded on its
/// own, so a line that is not UTF-8 is answered as a malformed request and the next is unaffected.
let serve (input: Stream) (output: Stream) : unit =
    let out = new StreamWriter(output, UTF8Encoding(false))
    out.NewLine <- "\n"
    let buffer = new MemoryStream()

    let respond () =
        let bytes = buffer.ToArray()
        buffer.SetLength 0L

        let bytes =
            if bytes.Length > 0 && bytes[bytes.Length - 1] = byte '\r' then
                bytes[.. bytes.Length - 2]
            else
                bytes

        let line =
            try
                Protocol.handle (strictUtf8.GetString bytes)
            with :? DecoderFallbackException ->
                Json.write (
                    Json.JObj
                        [ "protocol", Json.JStr Protocol.Version
                          "id", Json.JNull
                          "error",
                          Json.JObj
                              [ "code", Json.JStr Protocol.ErrorCode.MalformedRequest
                                "message", Json.JStr "the line is not UTF-8" ] ]
                )

        out.WriteLine line
        out.Flush()

    let mutable b = input.ReadByte()

    while b >= 0 do
        if b = int '\n' then
            respond ()
        else
            buffer.WriteByte(byte b)

        b <- input.ReadByte()

    // A final line with no terminating LF is still a request.
    if buffer.Length > 0L then
        respond ()

    out.Flush()

/// The extracted models recurse structurally and are not tail-recursive, so the protocol runs on a
/// thread with a stack large enough for any input inside the protocol's limits.
let private onBigStack (work: unit -> unit) =
    let t = Thread(work, 512 * 1024 * 1024)
    t.Start()
    t.Join()

[<EntryPoint>]
let main argv =
    match argv with
    | [||] ->
        use stdin = Console.OpenStandardInput()
        use stdout = Console.OpenStandardOutput()
        onBigStack (fun () -> serve (new BufferedStream(stdin)) stdout)
        0
    | [| "--version" |] ->
        printfn "fuaran-core-oracle %s (protocol %s)" Protocol.oracleVersion Protocol.Version
        0
    | _ ->
        eprintfn "usage: fuaran-core-oracle [--version]"
        eprintfn "  Serves the oracle protocol on stdin/stdout, one JSON request per line (docs/oracle-protocol.md)."
        2
