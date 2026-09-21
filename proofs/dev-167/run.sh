#!/bin/bash
# usage: run.sh <Module> [extra fstar args]
W=/c/repos/Fuaran-ToolUp/.claude/campaign-runs/camp-core-programme-20260920/w167
F=/c/repos/Fuaran-ToolUp/Fuaran/Fuaran-Core/proofs/.fstar/fstar/bin/fstar.exe
export FSTAR_HOME='C:\repos\Fuaran-ToolUp\Fuaran\Fuaran-Core\proofs\.fstar\fstar'
m=$1; shift
cd $W/dev
s=$(date +%s)
timeout 540 $F --z3rlimit 40 --report_assumes error --cache_checked_modules --cache_dir $W/cache --include /c/repos/Fuaran-ToolUp/.claude/campaign-runs/167/proofs "$@" $m.fst > $W/dev/$m.log 2>&1
echo "exit $? $(( $(date +%s)-s ))s"
grep -n "Error\|error\|Failed\|failed" -A12 $W/dev/$m.log | head -${LINES_MAX:-70}
