namespace Fuaran.Core

// ============================================================================
//  The seam-refusal family (Phase 551, DECISIONS.md D200) — the seams' refusal and
//  declaration unions, read through the codecs DERIVED from their IDL declaration
//  (`CapabilitySeamCodec`, `PipelineSeamCodec`, `QuerySeamCodec`,
//  `FieldChangeSeamCodec`, `RejectionSeamCodec`). Each vector is an AUTHORED pair:
//  a document and what its reader must answer — the same bytes back, or a refusal
//  with its code at its path. A host that reads a seam document certifies its
//  reader by reproducing every outcome (D188: hosts certify on code and path; the
//  sentence is a projection of them). The vectors are exported to
//  `conformance/refusals/seam-refusals.json` (Core-internal; no corpus copy).
// ============================================================================

/// The seam-refusal vectors and the law that holds the derived seam codecs to them.
module internal SeamRefusalLaws =

    /// What a vector's reader must answer.
    type Expected =
        /// The document reads, and its value re-encodes to the document's own canonical bytes.
        | Reads
        /// The document is refused with this decode code at this path.
        | Refused of code: DecodeCode * path: PathSegment list

    /// One authored vector: the reader it is for, its name, the document, and the outcome.
    type Vector =
        { Reader: string
          Name: string
          Document: string
          Expected: Expected }

    let private str: JVal -> Result<string, DecodeError> = Decoder.str

    /// A reader: the decode, then the value re-encoded (an acceptance's bytes are the re-encoding).
    let private reader (decode: JVal -> Result<'T, DecodeError>) (encode: 'T -> JVal) =
        fun (j: JVal) -> decode j |> Result.map encode

    /// Every reader a vector may name. `/strict` is the derived strict-members reading.
    let readers: (string * (JVal -> Result<JVal, DecodeError>)) list =
        [ "invokeError", reader CapabilitySeamCodec.decodeInvokeError CapabilitySeamCodec.encodeInvokeError
          "invokeError/strict", reader CapabilitySeamCodec.decodeInvokeErrorStrict CapabilitySeamCodec.encodeInvokeError
          "placement", reader CapabilitySeamCodec.decodePlacement CapabilitySeamCodec.encodePlacement
          "placement/strict", reader CapabilitySeamCodec.decodePlacementStrict CapabilitySeamCodec.encodePlacement
          "deferred", reader (CapabilitySeamCodec.decodeDeferred str) (CapabilitySeamCodec.encodeDeferred JStr)
          "deferred/strict",
          reader (CapabilitySeamCodec.decodeDeferredStrict str) (CapabilitySeamCodec.encodeDeferred JStr)
          "pipeline", reader PipelineSeamCodec.decodeCapabilityPipeline PipelineSeamCodec.encodeCapabilityPipeline
          "pipeline/strict",
          reader PipelineSeamCodec.decodeCapabilityPipelineStrict PipelineSeamCodec.encodeCapabilityPipeline
          "declarationFault",
          reader CapabilitySeamCodec.decodeDeclarationFault CapabilitySeamCodec.encodeDeclarationFault
          "declarationFault/strict",
          reader CapabilitySeamCodec.decodeDeclarationFaultStrict CapabilitySeamCodec.encodeDeclarationFault
          "pipelineNode", reader PipelineSeamCodec.decodePipelineNode PipelineSeamCodec.encodePipelineNode
          "pipelineNode/strict", reader PipelineSeamCodec.decodePipelineNodeStrict PipelineSeamCodec.encodePipelineNode
          "argSource", reader PipelineSeamCodec.decodeArgSource PipelineSeamCodec.encodeArgSource
          "argSource/strict", reader PipelineSeamCodec.decodeArgSourceStrict PipelineSeamCodec.encodeArgSource
          "queryError/strict", reader QuerySeamCodec.decodeQueryErrorStrict QuerySeamCodec.encodeQueryError
          "fieldChange/strict",
          reader FieldChangeSeamCodec.decodeFieldChangeStrict FieldChangeSeamCodec.encodeFieldChange
          "rejection/strict",
          reader (RejectionSeamCodec.decodeRejectionStrict str) (RejectionSeamCodec.encodeRejection JStr)
          "decodeError", reader DecodeError.decoder DecodeError.toJson
          "queryError", reader QuerySeamCodec.decodeQueryError QuerySeamCodec.encodeQueryError
          "columnPredicate", reader QuerySeamCodec.decodeColumnPredicate QuerySeamCodec.encodeColumnPredicate
          "columnPredicate/strict",
          reader QuerySeamCodec.decodeColumnPredicateStrict QuerySeamCodec.encodeColumnPredicate
          "sortKey", reader QuerySeamCodec.decodeSortKey QuerySeamCodec.encodeSortKey
          "sortKey/strict", reader QuerySeamCodec.decodeSortKeyStrict QuerySeamCodec.encodeSortKey
          "fieldChange", reader FieldChangeSeamCodec.decodeFieldChange FieldChangeSeamCodec.encodeFieldChange
          "rejection", reader (RejectionSeamCodec.decodeRejection str) (RejectionSeamCodec.encodeRejection JStr) ]

    let private k (name: string) = PathSegment.Key name
    let private i (n: int) = PathSegment.Index n

    let private reads reader name doc =
        { Reader = reader
          Name = name
          Document = doc
          Expected = Reads }

    let private refused reader name doc code path =
        { Reader = reader
          Name = name
          Document = doc
          Expected = Refused(code, path) }

    let private invokeTags =
        "'noSuchCapability', 'duplicateCapability', 'unknownArg', 'argOutOfSpace', 'requiredArgsUnbound', 'uninvocableArg', 'bodyFailed', 'nonTotalCapability', 'illFormedCapability', 'duplicateArg', 'policyRefused', 'approvalRequired'"

    /// The vectors, by reader: every case of every union read back, then its readers' refusals.
    let vectors: Vector list =
        [
          // ---- InvokeError (and DeclarationFault inside illFormedCapability) ----
          for name, doc in
              [ "noSuchCapability", """{"$type":"noSuchCapability","id":"x","known":["a","b"]}"""
                "duplicateCapability", """{"$type":"duplicateCapability","id":"x"}"""
                "unknownArg", """{"$type":"unknownArg","addr":"a","declared":["b","c"]}"""
                "argOutOfSpace",
                """{"$type":"argOutOfSpace","addr":"a","got":"9","space":{"$type":"intRange","max":1,"min":-1}}"""
                "requiredArgsUnbound", """{"$type":"requiredArgsUnbound","addrs":["a","b"]}"""
                "uninvocableArg", """{"$type":"uninvocableArg","addr":"a"}"""
                "bodyFailed", """{"$type":"bodyFailed","reason":"boom"}"""
                "nonTotalCapability", """{"$type":"nonTotalCapability","addrs":["r"],"id":"x"}"""
                "illFormedCapability-emptySpace",
                """{"$type":"illFormedCapability","fault":{"$type":"emptySpace","addr":"a","space":{"$type":"enum","values":[]}},"id":"x"}"""
                "illFormedCapability-nonFiniteBound",
                """{"$type":"illFormedCapability","fault":{"$type":"nonFiniteBound","addr":"b"},"id":"x"}"""
                "illFormedCapability-duplicateHoleAddr",
                """{"$type":"illFormedCapability","fault":{"$type":"duplicateHoleAddr","addr":"c"},"id":"x"}"""
                "illFormedCapability-holeUnderSlot",
                """{"$type":"illFormedCapability","fault":{"$type":"holeUnderSlot","node":"n"},"id":"x"}"""
                "duplicateArg", """{"$type":"duplicateArg","addr":"a"}"""
                "policyRefused", """{"$type":"policyRefused","allowed":["a"],"policy":"p","reason":"r"}"""
                "approvalRequired", """{"$type":"approvalRequired","policy":"p"}""" ] do
              yield reads "invokeError" name doc
          yield refused "invokeError" "not-an-object" "[]" DecodeCode.WrongKind []
          yield refused "invokeError" "no-tag" "{}" DecodeCode.MissingField [ k "$type" ]
          yield refused "invokeError" "tag-not-a-string" """{"$type":1}""" DecodeCode.WrongKind [ k "$type" ]
          yield refused "invokeError" "unknown-tag" """{"$type":"nope"}""" DecodeCode.UnknownTag [ k "$type" ]
          yield
              refused
                  "invokeError"
                  "query-seam-tag"
                  """{"$type":"queryPolicyRefused","allowed":[],"policy":"p","reason":"r"}"""
                  DecodeCode.UnknownTag
                  [ k "$type" ]
          yield refused "invokeError" "missing-id" """{"$type":"noSuchCapability"}""" DecodeCode.MissingField [ k "id" ]
          yield
              refused
                  "invokeError"
                  "known-item-not-a-string"
                  """{"$type":"noSuchCapability","id":"x","known":[1]}"""
                  DecodeCode.WrongKind
                  [ k "known"; i 0 ]
          yield
              refused
                  "invokeError"
                  "space-bound-not-an-int"
                  """{"$type":"argOutOfSpace","addr":"a","got":"g","space":{"$type":"intRange","min":"x","max":1}}"""
                  DecodeCode.WrongKind
                  [ k "space"; k "min" ]
          yield
              refused
                  "invokeError"
                  "fault-unknown-tag"
                  """{"$type":"illFormedCapability","id":"x","fault":{"$type":"nope"}}"""
                  DecodeCode.UnknownTag
                  [ k "fault"; k "$type" ]
          yield
              refused
                  "invokeError"
                  "fault-missing-node"
                  """{"$type":"illFormedCapability","id":"x","fault":{"$type":"holeUnderSlot"}}"""
                  DecodeCode.MissingField
                  [ k "fault"; k "node" ]
          yield
              refused
                  "invokeError"
                  "missing-allowed"
                  """{"$type":"policyRefused","policy":"p","reason":"r"}"""
                  DecodeCode.MissingField
                  [ k "allowed" ]
          yield
              refused
                  "invokeError/strict"
                  "undeclared-member"
                  """{"$type":"duplicateArg","addr":"a","x":1}"""
                  DecodeCode.UndeclaredMember
                  [ k "x" ]

          yield
              reads
                  "declarationFault"
                  "emptySpace"
                  """{"$type":"emptySpace","addr":"a","space":{"$type":"enum","values":[]}}"""
          yield
              refused
                  "declarationFault/strict"
                  "undeclared-member"
                  """{"$type":"holeUnderSlot","node":"n","x":1}"""
                  DecodeCode.UndeclaredMember
                  [ k "x" ]

          // ---- Placement ----
          for name, doc in
              [ "buildTime", """{"$type":"buildTime"}"""
                "server", """{"$type":"server"}"""
                "clientDeclarative", """{"$type":"clientDeclarative"}"""
                "clientIsland-pyodide", """{"$type":"clientIsland","island":"pyodide"}"""
                "clientIsland-fable", """{"$type":"clientIsland","island":"fable"}"""
                "clientIsland-js", """{"$type":"clientIsland","island":"js"}"""
                "precomputed", """{"$type":"precomputed"}""" ] do
              yield reads "placement" name doc
          yield refused "placement" "unknown-tag" """{"$type":"moon"}""" DecodeCode.UnknownTag [ k "$type" ]
          yield
              refused "placement" "missing-island" """{"$type":"clientIsland"}""" DecodeCode.MissingField [ k "island" ]
          yield
              refused
                  "placement"
                  "unknown-island"
                  """{"$type":"clientIsland","island":"lua"}"""
                  DecodeCode.UnknownTag
                  [ k "island" ]
          yield
              refused
                  "placement"
                  "island-not-a-string"
                  """{"$type":"clientIsland","island":1}"""
                  DecodeCode.WrongKind
                  [ k "island" ]
          yield
              refused
                  "placement/strict"
                  "undeclared-member"
                  """{"$type":"server","x":1}"""
                  DecodeCode.UndeclaredMember
                  [ k "x" ]
          yield
              refused
                  "placement/strict"
                  "undeclared-member-of-a-case"
                  """{"$type":"clientIsland","island":"js","x":1}"""
                  DecodeCode.UndeclaredMember
                  [ k "x" ]
          // A tag the union does not declare has no roster: the strict read answers as the lenient.
          yield
              refused
                  "placement/strict"
                  "unknown-tag-with-a-member"
                  """{"$type":"moon","x":1}"""
                  DecodeCode.UnknownTag
                  [ k "$type" ]

          // ---- Deferred, over a string payload ----
          for name, doc in
              [ "pending", """{"$type":"pending"}"""
                "ready", """{"$type":"ready","value":"v"}"""
                "failed", """{"$type":"failed","message":"m"}""" ] do
              yield reads "deferred" name doc
          yield refused "deferred" "ready-missing-value" """{"$type":"ready"}""" DecodeCode.MissingField [ k "value" ]
          yield
              refused
                  "deferred"
                  "ready-payload-refused"
                  """{"$type":"ready","value":1}"""
                  DecodeCode.WrongKind
                  [ k "value" ]
          yield
              refused
                  "deferred"
                  "message-not-a-string"
                  """{"$type":"failed","message":2}"""
                  DecodeCode.WrongKind
                  [ k "message" ]
          yield refused "deferred" "unknown-tag" """{"$type":"later"}""" DecodeCode.UnknownTag [ k "$type" ]
          yield
              refused
                  "deferred/strict"
                  "undeclared-member"
                  """{"$type":"ready","value":"v","x":1}"""
                  DecodeCode.UndeclaredMember
                  [ k "x" ]

          // ---- the capability pipeline: both node cases and both argument sources ----
          yield
              reads
                  "pipeline"
                  "every-case"
                  """{"nodes":[{"$type":"source","dataRef":"ref","id":"s","outputType":{"$type":"intRange","max":9,"min":0}},{"$type":"invoke","args":[{"addr":"a","source":{"$type":"literal","value":"1"}},{"addr":"b","source":{"$type":"fromNode","node":"s"}}],"capabilityId":"cap","id":"i","outputType":{"$type":"anyString"}}]}"""
          yield refused "pipeline" "missing-nodes" "{}" DecodeCode.MissingField [ k "nodes" ]
          yield
              refused
                  "pipeline"
                  "unknown-node"
                  """{"nodes":[{"$type":"sink"}]}"""
                  DecodeCode.UnknownTag
                  [ k "nodes"; i 0; k "$type" ]
          yield
              refused
                  "pipeline"
                  "missing-output-type"
                  """{"nodes":[{"$type":"source","id":"s","dataRef":"r"}]}"""
                  DecodeCode.MissingField
                  [ k "nodes"; i 0; k "outputType" ]
          yield
              refused
                  "pipeline"
                  "ill-formed-output-type"
                  """{"nodes":[{"$type":"source","id":"s","dataRef":"r","outputType":{"$type":"enum","values":[]}}]}"""
                  DecodeCode.OutOfRange
                  [ k "nodes"; i 0; k "outputType" ]
          yield
              refused
                  "pipeline"
                  "unknown-source"
                  """{"nodes":[{"$type":"invoke","id":"i","capabilityId":"c","outputType":{"$type":"anyString"},"args":[{"addr":"a","source":{"$type":"constant"}}]}]}"""
                  DecodeCode.UnknownTag
                  [ k "nodes"; i 0; k "args"; i 0; k "source"; k "$type" ]
          yield
              refused
                  "pipeline"
                  "from-node-missing-node"
                  """{"nodes":[{"$type":"invoke","id":"i","capabilityId":"c","outputType":{"$type":"anyString"},"args":[{"addr":"a","source":{"$type":"fromNode","nodeId":3}}]}]}"""
                  DecodeCode.MissingField
                  [ k "nodes"; i 0; k "args"; i 0; k "source"; k "node" ]

          yield
              reads
                  "pipelineNode"
                  "source"
                  """{"$type":"source","dataRef":"ref","id":"s","outputType":{"$type":"anyString"}}"""
          yield reads "argSource" "literal" """{"$type":"literal","value":"1"}"""
          yield reads "argSource" "fromNode" """{"$type":"fromNode","node":"s"}"""
          yield
              refused
                  "argSource/strict"
                  "undeclared-member"
                  """{"$type":"fromNode","node":"s","x":1}"""
                  DecodeCode.UndeclaredMember
                  [ k "x" ]
          // An argument's `{addr, source}` pair is a hosted slot, its own reader's to police (D198): the
          // strict reading holds the node's own members.
          yield
              refused
                  "pipelineNode/strict"
                  "undeclared-member"
                  """{"$type":"source","dataRef":"ref","id":"s","outputType":{"$type":"anyString"},"x":1}"""
                  DecodeCode.UndeclaredMember
                  [ k "x" ]
          yield
              refused
                  "pipeline/strict"
                  "undeclared-member"
                  """{"nodes":[],"x":1}"""
                  DecodeCode.UndeclaredMember
                  [ k "x" ]

          // ---- QueryError (and ColumnPredicate inside predicateNotHonoured) ----
          for name, doc in
              [ "noSuchQuery", """{"$type":"noSuchQuery","id":"x","known":["a","b"]}"""
                "duplicateQuery", """{"$type":"duplicateQuery","id":"x"}"""
                "unknownParam", """{"$type":"unknownParam","declared":["a"],"name":"p"}"""
                "paramTypeMismatch", """{"$type":"paramTypeMismatch","expected":"int","got":"string","name":"p"}"""
                "requiredParamsUnbound", """{"$type":"requiredParamsUnbound","names":["a","b"]}"""
                "sourceNotResolved", """{"$type":"sourceNotResolved","ref":"s"}"""
                "executionFailed", """{"$type":"executionFailed","detail":"d","recoverable":["a"]}"""
                "timeout", """{"$type":"timeout"}"""
                "requiredParamsNull", """{"$type":"requiredParamsNull","names":["a"]}"""
                "duplicateParam", """{"$type":"duplicateParam","name":"a"}"""
                "queryPolicyRefused", """{"$type":"queryPolicyRefused","allowed":["a"],"policy":"p","reason":"r"}"""
                "queryApprovalRequired", """{"$type":"queryApprovalRequired","policy":"p"}"""
                "unreadableArgs",
                """{"$type":"unreadableArgs","error":{"code":"WrongKind","expected":"int","message":"expected int, got array","path":["year"]}}"""
                "unknownColumn", """{"$type":"unknownColumn","declared":["a"],"name":"c"}"""
                "predicateTypeMismatch",
                """{"$type":"predicateTypeMismatch","column":"c","expected":"int","got":"timestamp_ms"}"""
                "predicateNotApplicable",
                """{"$type":"predicateNotApplicable","column":"c","columnType":"decimal","predicate":"contains"}"""
                "illFormedLiteral", """{"$type":"illFormedLiteral","column":"c","reason":"why"}"""
                "duplicateSortColumn", """{"$type":"duplicateSortColumn","column":"c"}"""
                "predicateNotHonoured-equalTo",
                """{"$type":"predicateNotHonoured","predicate":{"$type":"equalTo","column":"c","type":"int","value":1}}"""
                "predicateNotHonoured-greaterThan",
                """{"$type":"predicateNotHonoured","predicate":{"$type":"greaterThan","column":"c","type":"float","value":1.5}}"""
                "predicateNotHonoured-atLeast",
                """{"$type":"predicateNotHonoured","predicate":{"$type":"atLeast","column":"c","type":"date","value":"2026-10-20"}}"""
                "predicateNotHonoured-lessThan",
                """{"$type":"predicateNotHonoured","predicate":{"$type":"lessThan","column":"c","type":"string","value":"m"}}"""
                "predicateNotHonoured-atMost",
                """{"$type":"predicateNotHonoured","predicate":{"$type":"atMost","column":"c","type":"bool","value":true}}"""
                "predicateNotHonoured-contains",
                """{"$type":"predicateNotHonoured","predicate":{"$type":"contains","column":"c","text":"t"}}"""
                "predicateNotHonoured-isNull",
                """{"$type":"predicateNotHonoured","predicate":{"$type":"isNull","column":"c"}}"""
                "predicateNotHonoured-isNotNull",
                """{"$type":"predicateNotHonoured","predicate":{"$type":"isNotNull","column":"c"}}"""
                "orderNotHonoured", """{"$type":"orderNotHonoured","column":"c"}""" ] do
              yield reads "queryError" name doc
          yield refused "queryError" "not-an-object" "[]" DecodeCode.WrongKind []
          yield refused "queryError" "no-tag" "{}" DecodeCode.MissingField [ k "$type" ]
          yield
              refused
                  "queryError"
                  "capability-seam-tag"
                  """{"$type":"policyRefused","allowed":[],"policy":"p","reason":"r"}"""
                  DecodeCode.UnknownTag
                  [ k "$type" ]
          yield
              refused
                  "queryError"
                  "unknown-column-type"
                  """{"$type":"paramTypeMismatch","name":"p","expected":"int","got":"blob"}"""
                  DecodeCode.UnknownTag
                  [ k "got" ]
          yield
              refused
                  "queryError"
                  "unknown-predicate-kind"
                  """{"$type":"predicateNotApplicable","predicate":"like","column":"c","columnType":"string"}"""
                  DecodeCode.UnknownTag
                  [ k "predicate" ]
          yield
              refused
                  "queryError"
                  "unknown-decode-code"
                  """{"$type":"unreadableArgs","error":{"code":"Nope","path":[],"expected":"e","message":"m"}}"""
                  DecodeCode.UnknownTag
                  [ k "error"; k "code" ]
          yield
              refused
                  "queryError"
                  "decode-path-not-a-path"
                  """{"$type":"unreadableArgs","error":{"code":"WrongKind","path":{},"expected":"e","message":"m"}}"""
                  DecodeCode.WrongKind
                  [ k "error"; k "path" ]
          yield
              refused
                  "queryError"
                  "literal-its-type-cannot-carry"
                  """{"$type":"predicateNotHonoured","predicate":{"$type":"equalTo","column":"c","type":"int","value":"x"}}"""
                  DecodeCode.OutOfRange
                  [ k "predicate"; k "value" ]
          yield
              refused
                  "queryError"
                  "unknown-predicate"
                  """{"$type":"predicateNotHonoured","predicate":{"$type":"between","column":"c"}}"""
                  DecodeCode.UnknownTag
                  [ k "predicate"; k "$type" ]
          yield
              refused
                  "queryError"
                  "recoverable-item-not-a-string"
                  """{"$type":"executionFailed","detail":"d","recoverable":[1]}"""
                  DecodeCode.WrongKind
                  [ k "recoverable"; i 0 ]

          yield
              refused
                  "queryError/strict"
                  "undeclared-member-of-a-predicate"
                  """{"$type":"predicateNotHonoured","predicate":{"$type":"isNull","column":"c","x":1}}"""
                  DecodeCode.UndeclaredMember
                  [ k "predicate"; k "x" ]
          // A decode refusal journalled inside `unreadableArgs` reads back verbatim (D188).
          yield
              reads
                  "decodeError"
                  "journalled-refusal"
                  """{"code":"WrongKind","expected":"int","message":"expected int, got array","path":["year"]}"""
          yield
              refused
                  "decodeError"
                  "missing-message"
                  """{"code":"WrongKind","expected":"int","path":[]}"""
                  DecodeCode.MissingField
                  [ k "message" ]

          // ---- ColumnPredicate and SortKey, as a declaration's `where` and `orderBy` read them ----
          yield
              refused
                  "columnPredicate"
                  "unknown-column-type"
                  """{"$type":"atLeast","column":"revenue","type":"blob","value":1.5}"""
                  DecodeCode.UnknownTag
                  [ k "type" ]
          yield
              refused
                  "columnPredicate"
                  "text-not-a-string"
                  """{"$type":"contains","column":"region","text":1}"""
                  DecodeCode.WrongKind
                  [ k "text" ]
          yield
              refused
                  "columnPredicate/strict"
                  "undeclared-member"
                  """{"$type":"isNotNull","column":"region","x":1}"""
                  DecodeCode.UndeclaredMember
                  [ k "x" ]
          yield
              refused
                  "columnPredicate/strict"
                  "unknown-tag-with-a-member"
                  """{"$type":"like","column":"region","text":"U"}"""
                  DecodeCode.UnknownTag
                  [ k "$type" ]
          yield reads "sortKey" "descending" """{"column":"revenue","direction":"descending"}"""
          yield reads "sortKey" "ascending" """{"column":"region","direction":"ascending"}"""
          yield
              refused
                  "sortKey"
                  "unknown-direction"
                  """{"column":"revenue","direction":"down"}"""
                  DecodeCode.UnknownTag
                  [ k "direction" ]
          yield
              refused "sortKey" "missing-column" """{"direction":"descending"}""" DecodeCode.MissingField [ k "column" ]
          yield
              refused
                  "sortKey/strict"
                  "undeclared-member"
                  """{"column":"region","direction":"ascending","x":1}"""
                  DecodeCode.UndeclaredMember
                  [ k "x" ]

          // ---- FieldChange, discriminated by `member` ----
          for name, doc in
              [ "unit", """{"member":"unit","to":"m"}"""
                "label", """{"from":"x","member":"label"}"""
                "description", """{"from":"d","member":"description","to":"e"}"""
                "ext", """{"key":"k","member":"ext","to":"v"}""" ] do
              yield reads "fieldChange" name doc
          yield
              refused
                  "fieldChange"
                  "unknown-member"
                  """{"member":"colour","to":"m"}"""
                  DecodeCode.UnknownTag
                  [ k "member" ]
          yield
              refused
                  "fieldChange"
                  "unit-the-kit-does-not-parse"
                  """{"member":"unit","to":"furlongs per quark"}"""
                  DecodeCode.OutOfRange
                  [ k "to" ]
          yield
              refused
                  "fieldChange"
                  "key-not-a-string"
                  """{"key":7,"member":"ext","to":"v"}"""
                  DecodeCode.WrongKind
                  [ k "key" ]
          yield
              refused
                  "fieldChange"
                  "from-not-a-string"
                  """{"from":1,"member":"label"}"""
                  DecodeCode.WrongKind
                  [ k "from" ]

          yield
              refused
                  "fieldChange/strict"
                  "undeclared-member"
                  """{"member":"label","to":"t","x":1}"""
                  DecodeCode.UndeclaredMember
                  [ k "x" ]

          // ---- Rejection, over string ids ----
          for name, doc in
              [ "unknownNode", """{"$type":"unknownNode","addressable":["a","b"],"target":"t"}"""
                "duplicateId", """{"$type":"duplicateId","id":"d"}"""
                "cannotRemoveRoot", """{"$type":"cannotRemoveRoot"}"""
                "wouldNestUnderSelf-self", """{"$type":"wouldNestUnderSelf","relation":"self","target":"t"}"""
                "wouldNestUnderSelf-descendant",
                """{"$type":"wouldNestUnderSelf","relation":"descendant","target":"t"}"""
                "notAContainer", """{"$type":"notAContainer","kindTag":"k","target":"t"}"""
                "reorderMismatch", """{"$type":"reorderMismatch","expected":["a"],"got":["b"],"parent":"p"}"""
                "rejected", """{"$type":"rejected","code":"c","message":"m"}"""
                "keyedPosition", """{"$type":"keyedPosition","holder":"h","slot":2,"target":"t"}"""
                "illegalChild",
                """{"$type":"illegalChild","child":"c","childKind":"ck","legal":["x","y"],"parent":"p","parentKind":"pk"}"""
                "stillReferenced", """{"$type":"stillReferenced","referrers":["r"],"target":"t"}"""
                "rootIdMismatch", """{"$type":"rootIdMismatch","after":"b","before":"a"}""" ] do
              yield reads "rejection" name doc
          yield refused "rejection" "unknown-tag" """{"$type":"forbidden"}""" DecodeCode.UnknownTag [ k "$type" ]
          yield
              refused
                  "rejection"
                  "unknown-relation"
                  """{"$type":"wouldNestUnderSelf","relation":"sideways","target":"t"}"""
                  DecodeCode.UnknownTag
                  [ k "relation" ]
          yield
              refused
                  "rejection"
                  "slot-not-an-int"
                  """{"$type":"keyedPosition","holder":"h","slot":"2","target":"t"}"""
                  DecodeCode.WrongKind
                  [ k "slot" ]
          yield
              refused
                  "rejection/strict"
                  "undeclared-member"
                  """{"$type":"duplicateId","id":"d","x":1}"""
                  DecodeCode.UndeclaredMember
                  [ k "x" ] ]

    /// What `v`'s reader answers: the re-encoded bytes, or the refusal (a parse failure included).
    let outcome (v: Vector) : Result<string, DecodeError> =
        match readers |> List.tryFind (fun (n, _) -> n = v.Reader) with
        | None -> failwithf "seam vector %s names no reader '%s'" v.Name v.Reader
        | Some(_, read) ->
            Decoder.parse ReadOptions.strict v.Document
            |> Result.bind read
            |> Result.map (Canonical.write Rendering.CrossHost)

    /// The law over a vector list: every vector's reader answers what the vector says, by bytes or by
    /// code and path.
    let lawsOver (vs: Vector list) : LawResult list =
        let reads =
            LawKit.LawCell "every accepted seam document reads back and re-encodes to its own bytes"

        let refuses =
            LawKit.LawCell "every refused seam document is refused with its code at its path"

        for v in vs do
            let label () =
                v.Reader + " " + v.Name + ": " + v.Document

            match v.Expected, outcome v with
            | Reads, Ok bytes -> reads.Check((bytes = v.Document), fun () -> label () + " re-encoded to " + bytes)
            | Reads, Error e -> reads.Check(false, fun () -> label () + " was refused: " + DecodeError.render e)
            | Refused(code, path), Error e ->
                refuses.Check(
                    (e.Code = code && e.Path = path),
                    fun () -> label () + " was refused " + DecodeError.render e
                )
            | Refused _, Ok bytes -> refuses.Check(false, fun () -> label () + " was read, as " + bytes)

        LawKit.results [ reads; refuses ]

    /// The family: the law over the authored vectors.
    let seamRefusalLaws (runner: LawKit.Runner) (_iterations: int) : LawResult list =
        ignore runner
        lawsOver vectors
