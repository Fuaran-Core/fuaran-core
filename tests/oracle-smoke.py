#!/usr/bin/env python3
"""Smoke test of the oracle protocol, driven from outside .NET (docs/oracle-protocol.md).

    python tests/oracle-smoke.py <command> [args...]

<command> starts the oracle: a released binary (`./fuaran-core-oracle-0.41.0-linux-x64`) or a built
assembly (`dotnet tools/Fuaran.Core.Oracle.Cli/bin/Release/net10.0/fuaran-core-oracle.dll`). The script
talks to it the way a host's fuzzer harness does: one process, a request written, its response read,
in turn. It exercises every function and every error code, a line that is not UTF-8, and the
`--version` switch. Standard library only. Exit 0 when every check holds; 1, naming each that does
not.
"""

import json
import subprocess
import sys

PROTOCOL = "1.0"
FUNCTIONS = {"oracle.describe", "json.parse", "wire.decode", "wire.render", "tree.apply", "chain.verify", "dag.fold"}


class Oracle:
    def __init__(self, command):
        self.proc = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE)

    def raw(self, line: bytes) -> dict:
        self.proc.stdin.write(line + b"\n")
        self.proc.stdin.flush()
        out = self.proc.stdout.readline()
        if not out:
            raise RuntimeError("the oracle closed its output: " + self.proc.stderr.read().decode("utf-8", "replace"))
        text = out.decode("utf-8")  # strict: a response that is not UTF-8 fails here
        if not text.endswith("\n"):
            raise RuntimeError("a response line has no LF terminator")
        return json.loads(text)

    def ask(self, fn: str, payload: dict, rid=None) -> dict:
        request = {"fn": fn, "input": payload}
        if rid is not None:
            request["id"] = rid
        return self.raw(json.dumps(request).encode("utf-8"))

    def close(self) -> int:
        self.proc.stdin.close()
        rest = self.proc.stdout.read()
        code = self.proc.wait(timeout=60)
        if rest:
            raise RuntimeError("output after the last request: %r" % rest)
        return code


failures = []


def check(label: str, ok: bool, detail=""):
    print(("ok   " if ok else "FAIL ") + label)
    if not ok:
        failures.append(label + (": " + str(detail) if detail else ""))


def answer(resp: dict):
    return resp.get("answer")


def main(command):
    version = subprocess.run(command + ["--version"], capture_output=True, text=True, timeout=60)
    check("--version exits 0 and names the protocol", version.returncode == 0 and ("protocol " + PROTOCOL) in version.stdout, version)

    o = Oracle(command)

    r = o.ask("oracle.describe", {}, rid="d")
    desc = (answer(r) or {}).get("description", {})
    check("every response carries the protocol version", r.get("protocol") == PROTOCOL, r)
    check("a string id is echoed", r.get("id") == "d", r)
    check("describe lists the protocol's functions", {f["name"] for f in desc.get("functions", [])} == FUNCTIONS, desc)

    r = o.ask("json.parse", {"text": '{"a":[1,2.5,"\\u00e9"],"b":true}'}, rid=1)
    check(
        "json.parse answers a value, keeping each number's token",
        answer(r) == {"value": {"object": [
            {"key": "a", "value": {"array": [{"int": "1"}, {"float": "2.5"}, {"string": "é"}]}},
            {"key": "b", "value": {"bool": True}}]}},
        r,
    )
    r = o.ask("json.parse", {"text": "[1,"})
    check("json.parse refuses a truncated document, naming the unit", answer(r) == {"parseRefused": {"at": 3, "kind": "UnexpectedEndOfInput", "message": "unexpected end of input"}}, r)
    r = o.ask("json.parse", {"text": "[[[1]]]", "limits": {"depth": 2}})
    check("json.parse answers a limit", answer(r) == {"parseLimit": {"at": 2, "limit": "depth"}}, r)

    r = o.ask("wire.decode", {"text": '{"kind":"group","id":"g","items":[{"kind":"text","value":"hi"}]}'})
    check("wire.decode answers a node", answer(r) == {"node": {"id": "g", "items": [{"kind": "text", "value": "hi"}], "kind": "group"}}, r)
    r = o.ask("wire.decode", {"text": '{"kind":"zzz"}'})
    check("wire.decode refuses an unknown kind", answer(r) == {"decodeRefused": {"message": "unknown kind: zzz"}}, r)

    r = o.ask("wire.render", {"value": {"object": [{"key": "b", "value": {"int": "1"}}, {"key": "a", "value": {"float": "-0.0"}}]}})
    check("wire.render sorts keys and collapses -0", answer(r) == {"rendered": '{"a":0,"b":1}'}, r)
    # A lone surrogate: Python's json writes it as \ud800, and the oracle answers it the same way.
    r = o.ask("wire.render", {"value": {"string": "\ud800"}})
    check("wire.render refuses an unpaired surrogate and hands it back", answer(r) == {"refusedString": {"isKey": False, "path": [], "string": "\ud800"}}, r)
    r = o.ask("wire.render", {"value": {"float": "NaN"}})
    check("wire.render refuses a non-finite float", answer(r) == {"refusedNonFinite": {"path": [], "value": "NaN"}}, r)

    tree = {"id": "r", "kind": "doc", "children": [{"id": "a", "kind": "s"}]}
    r = o.ask("tree.apply", {"tree": tree, "op": {"op": "removeNode", "node": "a"}})
    check("tree.apply applies", answer(r) == {"applied": {"children": [], "id": "r", "kind": "doc"}}, r)
    r = o.ask("tree.apply", {"tree": tree, "op": {"op": "removeNode", "node": "r"}})
    check("tree.apply rejects removing the root", answer(r) == {"rejected": {"rejection": "cannotRemoveRoot"}}, r)

    r = o.ask("chain.verify", {"hash": "fnv1a", "records": [{"seq": 0, "actor": "A", "op": "x", "prev": "", "hash": "d32b41db"}]})
    check("chain.verify finds a true FNV-1a chain intact", answer(r) == {"intact": {}}, r)
    r = o.ask("chain.verify", {"hash": "sha256", "records": [{"seq": 1, "actor": "A", "op": "x", "prev": "", "hash": "h"}]})
    check("chain.verify names the first break", (answer(r) or {}).get("broken", {}).get("reason") == "sequence-number mismatch", r)

    lane = lambda node: [{"op": "insertChild", "parent": "r", "node": {"id": node, "kind": "s"}}]
    r = o.ask("dag.fold", {"base": {"id": "r", "kind": "doc"}, "lanes": [lane("a"), []]})
    check("dag.fold folds independent lanes", answer(r) == {"folded": {"children": [{"children": [], "id": "a", "kind": "s"}], "id": "r", "kind": "doc"}}, r)
    r = o.ask("dag.fold", {"base": {"id": "r", "kind": "doc"}, "lanes": [lane("a"), lane("b")]})
    check("dag.fold halts on an insert-position clash", [c["shape"] for c in (answer(r) or {}).get("halted", [])] == ["insert-position-clash"], r)

    r = o.raw(b"not json")
    check("a line that is not JSON is a malformed request", r.get("error", {}).get("code") == "malformed-request" and r.get("id") is None, r)
    r = o.raw(b'{"fn":"json.parse","input":{"text":"\xff"}}')
    check("a line that is not UTF-8 is a malformed request", r.get("error", {}).get("code") == "malformed-request", r)
    r = o.ask("no.such", {}, rid=7)
    check("an unknown function is named as one", r.get("error", {}).get("code") == "unknown-function" and r.get("id") == 7, r)
    r = o.ask("tree.apply", {"tree": {"id": "r"}, "op": {"op": "removeNode", "node": "r"}})
    check("an input that does not fit is bad-input", r.get("error", {}).get("code") == "bad-input", r)
    r = o.ask("oracle.describe", {})
    check("the process still answers after every error", "description" in (answer(r) or {}), r)

    check("the oracle exits 0 when its input closes", o.close() == 0)

    if failures:
        print("\n%d check(s) failed:" % len(failures))
        for f in failures:
            print("  " + f)
        return 1
    print("\nall checks hold")
    return 0


if __name__ == "__main__":
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(2)
    sys.exit(main(sys.argv[1:]))
