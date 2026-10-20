namespace Fuaran.Core.Idl.Emit

// ---------------------------------------------------------------------------
// Phase 524 (B §8 S10) — the generated modules' fixed RUNTIME PRELUDES are template files, not
// string literals inside the emitters: the TypeScript decode prelude and its twin JSON reader, the
// collecting preludes of both backends, the hosted-format checks and the F# decode helpers. They
// are text the generator copies, unchanged, into what it emits; held as files under `Templates/`,
// embedded in this assembly by name, they read as the source they are (a JS file is a JS file)
// and leave the emitters holding only what varies per vocabulary. A template's only parameters
// are the `__NAME__` placeholders its emitter replaces.
// ---------------------------------------------------------------------------

/// The embedded template files.
module internal Template =

    let private assembly = System.Reflection.Assembly.GetExecutingAssembly()

    let private cache =
        System.Collections.Concurrent.ConcurrentDictionary<string, string>()

    /// The text of the template file `Templates/<name>`, exactly as committed. A name with no
    /// embedded file is a build defect, not an input, so it raises.
    let text (name: string) : string =
        cache.GetOrAdd(
            name,
            fun n ->
                use stream = assembly.GetManifestResourceStream("Templates/" + n)

                if isNull stream then
                    invalidOp ("the generator has no embedded template 'Templates/" + n + "'")

                use reader = new System.IO.StreamReader(stream, System.Text.UTF8Encoding(false))
                reader.ReadToEnd()
        )
