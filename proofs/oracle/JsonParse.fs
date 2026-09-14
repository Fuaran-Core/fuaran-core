module JsonParse

let rec rev_app = (fun ( l  :  Prims.list<'a> ) ( acc  :  Prims.list<'a> ) -> (match (l) with
| [] -> begin
     acc
     end
| (x)::t -> begin
     (rev_app t ((x)::acc))
     end))


let rev = (fun ( l  :  Prims.list<'a> ) -> (rev_app l []))

type ch =
| CSpace
| CTab
| CNewline
| CReturn
| CQuote
| CBackslash
| CSlash
| CLBrace
| CRBrace
| CLBrack
| CRBrack
| CColon
| CComma
| CMinus
| CPlus
| CDot
| CD0
| CD1
| CD2
| CD3
| CD4
| CD5
| CD6
| CD7
| CD8
| CD9
| CLa
| CLb
| CLc
| CLd
| CLe
| CLf
| CLl
| CLn
| CLr
| CLs
| CLt
| CLu
| CUa
| CUb
| CUc
| CUd
| CUe
| CUf
| COther of Prims.string


let uu___is_CSpace : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CSpace -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CTab : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CTab -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CNewline : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CNewline -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CReturn : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CReturn -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CQuote : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CQuote -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CBackslash : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CBackslash -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CSlash : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CSlash -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CLBrace : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CLBrace -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CRBrace : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CRBrace -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CLBrack : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CLBrack -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CRBrack : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CRBrack -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CColon : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CColon -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CComma : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CComma -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CMinus : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CMinus -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CPlus : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CPlus -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CDot : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CDot -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CD0 : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CD0 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CD1 : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CD1 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CD2 : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CD2 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CD3 : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CD3 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CD4 : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CD4 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CD5 : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CD5 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CD6 : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CD6 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CD7 : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CD7 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CD8 : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CD8 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CD9 : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CD9 -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CLa : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CLa -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CLb : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CLb -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CLc : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CLc -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CLd : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CLd -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CLe : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CLe -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CLf : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CLf -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CLl : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CLl -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CLn : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CLn -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CLr : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CLr -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CLs : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CLs -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CLt : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CLt -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CLu : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CLu -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CUa : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CUa -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CUb : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CUb -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CUc : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CUc -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CUd : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CUd -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CUe : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CUe -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_CUf : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CUf -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_COther : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| COther (c) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__COther__item__c : ch  ->  Prims.string = (fun ( projectee  :  ch ) -> (match (projectee) with
| COther (c) -> begin
     c
     end))


let ch_str : ch  ->  Prims.string = (fun ( c  :  ch ) -> (match (c) with
| CSpace -> begin
     " "
     end
| CTab -> begin
     "\t"
     end
| CNewline -> begin
     "\n"
     end
| CReturn -> begin
     "\r"
     end
| CQuote -> begin
     "\""
     end
| CBackslash -> begin
     "\\"
     end
| CSlash -> begin
     "/"
     end
| CLBrace -> begin
     "{"
     end
| CRBrace -> begin
     "}"
     end
| CLBrack -> begin
     "["
     end
| CRBrack -> begin
     "]"
     end
| CColon -> begin
     ":"
     end
| CComma -> begin
     ","
     end
| CMinus -> begin
     "-"
     end
| CPlus -> begin
     "+"
     end
| CDot -> begin
     "."
     end
| CD0 -> begin
     "0"
     end
| CD1 -> begin
     "1"
     end
| CD2 -> begin
     "2"
     end
| CD3 -> begin
     "3"
     end
| CD4 -> begin
     "4"
     end
| CD5 -> begin
     "5"
     end
| CD6 -> begin
     "6"
     end
| CD7 -> begin
     "7"
     end
| CD8 -> begin
     "8"
     end
| CD9 -> begin
     "9"
     end
| CLa -> begin
     "a"
     end
| CLb -> begin
     "b"
     end
| CLc -> begin
     "c"
     end
| CLd -> begin
     "d"
     end
| CLe -> begin
     "e"
     end
| CLf -> begin
     "f"
     end
| CLl -> begin
     "l"
     end
| CLn -> begin
     "n"
     end
| CLr -> begin
     "r"
     end
| CLs -> begin
     "s"
     end
| CLt -> begin
     "t"
     end
| CLu -> begin
     "u"
     end
| CUa -> begin
     "A"
     end
| CUb -> begin
     "B"
     end
| CUc -> begin
     "C"
     end
| CUd -> begin
     "D"
     end
| CUe -> begin
     "E"
     end
| CUf -> begin
     "F"
     end
| COther (s) -> begin
     s
     end))


let rec chs_str : Prims.list<ch>  ->  Prims.string = (fun ( l  :  Prims.list<ch> ) -> (match (l) with
| [] -> begin
     ""
     end
| (c)::t -> begin
     (Prims.strcat (ch_str c) (chs_str t))
     end))


let is_ws : ch  ->  Prims.bool = (fun ( c  :  ch ) -> ((((Prims.op_Equals c CSpace) || (Prims.op_Equals c CTab)) || (Prims.op_Equals c CNewline)) || (Prims.op_Equals c CReturn)))


let is_digit : ch  ->  Prims.bool = (fun ( c  :  ch ) -> ((((((((((Prims.op_Equals c CD0) || (Prims.op_Equals c CD1)) || (Prims.op_Equals c CD2)) || (Prims.op_Equals c CD3)) || (Prims.op_Equals c CD4)) || (Prims.op_Equals c CD5)) || (Prims.op_Equals c CD6)) || (Prims.op_Equals c CD7)) || (Prims.op_Equals c CD8)) || (Prims.op_Equals c CD9)))


let is_hex : ch  ->  Prims.bool = (fun ( c  :  ch ) -> (((((((((((((is_digit c) || (Prims.op_Equals c CLa)) || (Prims.op_Equals c CLb)) || (Prims.op_Equals c CLc)) || (Prims.op_Equals c CLd)) || (Prims.op_Equals c CLe)) || (Prims.op_Equals c CLf)) || (Prims.op_Equals c CUa)) || (Prims.op_Equals c CUb)) || (Prims.op_Equals c CUc)) || (Prims.op_Equals c CUd)) || (Prims.op_Equals c CUe)) || (Prims.op_Equals c CUf)))


let is_short_escape : ch  ->  Prims.bool = (fun ( c  :  ch ) -> ((((((((Prims.op_Equals c CQuote) || (Prims.op_Equals c CBackslash)) || (Prims.op_Equals c CSlash)) || (Prims.op_Equals c CLn)) || (Prims.op_Equals c CLr)) || (Prims.op_Equals c CLt)) || (Prims.op_Equals c CLb)) || (Prims.op_Equals c CLf)))


let is_exp : ch  ->  Prims.bool = (fun ( c  :  ch ) -> ((Prims.op_Equals c CLe) || (Prims.op_Equals c CUe)))

type och =
| OLit of ch
| OEsc of ch
| OUni of ch * ch * ch * ch


let uu___is_OLit : och  ->  Prims.bool = (fun ( projectee  :  och ) -> (match (projectee) with
| OLit (c) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__OLit__item__c : och  ->  ch = (fun ( projectee  :  och ) -> (match (projectee) with
| OLit (c) -> begin
     c
     end))


let uu___is_OEsc : och  ->  Prims.bool = (fun ( projectee  :  och ) -> (match (projectee) with
| OEsc (e) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__OEsc__item__e : och  ->  ch = (fun ( projectee  :  och ) -> (match (projectee) with
| OEsc (e) -> begin
     e
     end))


let uu___is_OUni : och  ->  Prims.bool = (fun ( projectee  :  och ) -> (match (projectee) with
| OUni (h1, h2, h3, h4) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__OUni__item__h1 : och  ->  ch = (fun ( projectee  :  och ) -> (match (projectee) with
| OUni (h1, h2, h3, h4) -> begin
     h1
     end))


let __proj__OUni__item__h2 : och  ->  ch = (fun ( projectee  :  och ) -> (match (projectee) with
| OUni (h1, h2, h3, h4) -> begin
     h2
     end))


let __proj__OUni__item__h3 : och  ->  ch = (fun ( projectee  :  och ) -> (match (projectee) with
| OUni (h1, h2, h3, h4) -> begin
     h3
     end))


let __proj__OUni__item__h4 : och  ->  ch = (fun ( projectee  :  och ) -> (match (projectee) with
| OUni (h1, h2, h3, h4) -> begin
     h4
     end))

type jval =
| JStr of Prims.list<och>
| JInt of Prims.list<ch>
| JBool of Prims.bool
| JFloat of Prims.list<ch>
| JArr of Prims.list<jval>
| JObj of Prims.list<(Prims.list<och> * jval)>


let uu___is_JStr : jval  ->  Prims.bool = (fun ( projectee  :  jval ) -> (match (projectee) with
| JStr (s) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JStr__item__s : jval  ->  Prims.list<och> = (fun ( projectee  :  jval ) -> (match (projectee) with
| JStr (s) -> begin
     s
     end))


let uu___is_JInt : jval  ->  Prims.bool = (fun ( projectee  :  jval ) -> (match (projectee) with
| JInt (tok) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JInt__item__tok : jval  ->  Prims.list<ch> = (fun ( projectee  :  jval ) -> (match (projectee) with
| JInt (tok) -> begin
     tok
     end))


let uu___is_JBool : jval  ->  Prims.bool = (fun ( projectee  :  jval ) -> (match (projectee) with
| JBool (b) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JBool__item__b : jval  ->  Prims.bool = (fun ( projectee  :  jval ) -> (match (projectee) with
| JBool (b) -> begin
     b
     end))


let uu___is_JFloat : jval  ->  Prims.bool = (fun ( projectee  :  jval ) -> (match (projectee) with
| JFloat (tok) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JFloat__item__tok : jval  ->  Prims.list<ch> = (fun ( projectee  :  jval ) -> (match (projectee) with
| JFloat (tok) -> begin
     tok
     end))


let uu___is_JArr : jval  ->  Prims.bool = (fun ( projectee  :  jval ) -> (match (projectee) with
| JArr (items) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JArr__item__items : jval  ->  Prims.list<jval> = (fun ( projectee  :  jval ) -> (match (projectee) with
| JArr (items) -> begin
     items
     end))


let uu___is_JObj : jval  ->  Prims.bool = (fun ( projectee  :  jval ) -> (match (projectee) with
| JObj (fields) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__JObj__item__fields : jval  ->  Prims.list<(Prims.list<och> * jval)> = (fun ( projectee  :  jval ) -> (match (projectee) with
| JObj (fields) -> begin
     fields
     end))

type ekind =
| UnexpectedChar
| UnexpectedEndOfInput
| ExpectedToken
| UnterminatedString
| UnterminatedEscape
| TruncatedUnicodeEscape
| BadEscape
| BadHexDigit
| MalformedNumber
| NullNotRepresentable
| MaxDepthExceeded
| TrailingCharacters


let uu___is_UnexpectedChar : ekind  ->  Prims.bool = (fun ( projectee  :  ekind ) -> (match (projectee) with
| UnexpectedChar -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_UnexpectedEndOfInput : ekind  ->  Prims.bool = (fun ( projectee  :  ekind ) -> (match (projectee) with
| UnexpectedEndOfInput -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_ExpectedToken : ekind  ->  Prims.bool = (fun ( projectee  :  ekind ) -> (match (projectee) with
| ExpectedToken -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_UnterminatedString : ekind  ->  Prims.bool = (fun ( projectee  :  ekind ) -> (match (projectee) with
| UnterminatedString -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_UnterminatedEscape : ekind  ->  Prims.bool = (fun ( projectee  :  ekind ) -> (match (projectee) with
| UnterminatedEscape -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_TruncatedUnicodeEscape : ekind  ->  Prims.bool = (fun ( projectee  :  ekind ) -> (match (projectee) with
| TruncatedUnicodeEscape -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_BadEscape : ekind  ->  Prims.bool = (fun ( projectee  :  ekind ) -> (match (projectee) with
| BadEscape -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_BadHexDigit : ekind  ->  Prims.bool = (fun ( projectee  :  ekind ) -> (match (projectee) with
| BadHexDigit -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_MalformedNumber : ekind  ->  Prims.bool = (fun ( projectee  :  ekind ) -> (match (projectee) with
| MalformedNumber -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_NullNotRepresentable : ekind  ->  Prims.bool = (fun ( projectee  :  ekind ) -> (match (projectee) with
| NullNotRepresentable -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_MaxDepthExceeded : ekind  ->  Prims.bool = (fun ( projectee  :  ekind ) -> (match (projectee) with
| MaxDepthExceeded -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_TrailingCharacters : ekind  ->  Prims.bool = (fun ( projectee  :  ekind ) -> (match (projectee) with
| TrailingCharacters -> begin
     true
     end
| uu___ -> begin
     false
     end))

type policy =
| RejectNull
| EraseMemberNull


let uu___is_RejectNull : policy  ->  Prims.bool = (fun ( projectee  :  policy ) -> (match (projectee) with
| RejectNull -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_EraseMemberNull : policy  ->  Prims.bool = (fun ( projectee  :  policy ) -> (match (projectee) with
| EraseMemberNull -> begin
     true
     end
| uu___ -> begin
     false
     end))

type pres<'a> =
| POk of 'a * Prims.list<ch>
| PErr of ekind * Prims.string * Prims.list<ch>


let uu___is_POk = (fun ( projectee  :  pres<'a> ) -> (match (projectee) with
| POk (v, rest) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__POk__item__v = (fun ( projectee  :  pres<'a> ) -> (match (projectee) with
| POk (v, rest) -> begin
     v
     end))


let __proj__POk__item__rest = (fun ( projectee  :  pres<'a> ) -> (match (projectee) with
| POk (v, rest) -> begin
     rest
     end))


let uu___is_PErr = (fun ( projectee  :  pres<'a> ) -> (match (projectee) with
| PErr (k, msg, at) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__PErr__item__k = (fun ( projectee  :  pres<'a> ) -> (match (projectee) with
| PErr (k, msg, at) -> begin
     k
     end))


let __proj__PErr__item__msg = (fun ( projectee  :  pres<'a> ) -> (match (projectee) with
| PErr (k, msg, at) -> begin
     msg
     end))


let __proj__PErr__item__at = (fun ( projectee  :  pres<'a> ) -> (match (projectee) with
| PErr (k, msg, at) -> begin
     at
     end))

type jresult =
| ROk of jval
| RErr of ekind * Prims.string * Prims.list<ch>


let uu___is_ROk : jresult  ->  Prims.bool = (fun ( projectee  :  jresult ) -> (match (projectee) with
| ROk (v) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__ROk__item__v : jresult  ->  jval = (fun ( projectee  :  jresult ) -> (match (projectee) with
| ROk (v) -> begin
     v
     end))


let uu___is_RErr : jresult  ->  Prims.bool = (fun ( projectee  :  jresult ) -> (match (projectee) with
| RErr (k, msg, at) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RErr__item__k : jresult  ->  ekind = (fun ( projectee  :  jresult ) -> (match (projectee) with
| RErr (k, msg, at) -> begin
     k
     end))


let __proj__RErr__item__msg : jresult  ->  Prims.string = (fun ( projectee  :  jresult ) -> (match (projectee) with
| RErr (k, msg, at) -> begin
     msg
     end))


let __proj__RErr__item__at : jresult  ->  Prims.list<ch> = (fun ( projectee  :  jresult ) -> (match (projectee) with
| RErr (k, msg, at) -> begin
     at
     end))


let msg_expect_char : ch  ->  Prims.string = (fun ( c  :  ch ) -> (Prims.strcat "expected \'" (Prims.strcat (ch_str c) "\'")))


let msg_expect_lit : Prims.string  ->  Prims.string = (fun ( l  :  Prims.string ) -> (Prims.strcat "expected \'" (Prims.strcat l "\'")))


let msg_unexpected_char : ch  ->  Prims.string = (fun ( c  :  ch ) -> (Prims.strcat "unexpected character \'" (Prims.strcat (ch_str c) "\'")))


let msg_bad_escape : ch  ->  Prims.string = (fun ( e  :  ch ) -> (Prims.strcat "bad escape \'\\" (Prims.strcat (ch_str e) "\'")))


let msg_malformed : Prims.list<ch>  ->  Prims.string = (fun ( tok  :  Prims.list<ch> ) -> (Prims.strcat "malformed number: " (chs_str tok)))


let msg_nonfinite : Prims.list<ch>  ->  Prims.string = (fun ( tok  :  Prims.list<ch> ) -> (Prims.strcat "number outside the finite double range; it cannot round-trip on the wire: " (chs_str tok)))


let msg_int53 : Prims.list<ch>  ->  Prims.string = (fun ( tok  :  Prims.list<ch> ) -> (Prims.strcat "integer literal outside the int53 safe range (|n| > 2^53); it cannot round-trip without precision loss: " (chs_str tok)))


let msg_null_strict : Prims.string = "null is not representable in the Fuaran wire JVal model"


let msg_null_tolerant : Prims.string = "null is not representable in the Fuaran wire JVal model, and this position has no absence to erase it to (only an object-member null is erased)"


let msg_depth : Prims.string  ->  Prims.string = (fun ( rendered_cap  :  Prims.string ) -> (Prims.strcat "max nesting depth " (Prims.strcat rendered_cap " exceeded")))


let rec len_lt : Prims.list<ch>  ->  Prims.list<ch>  ->  Prims.bool = (fun ( x  :  Prims.list<ch> ) ( y  :  Prims.list<ch> ) -> (match (((x), (y))) with
| ([], []) -> begin
     false
     end
| ([], (uu___)::uu___1) -> begin
     true
     end
| ((uu___)::uu___1, []) -> begin
     false
     end
| ((uu___)::xt, (uu___1)::yt) -> begin
     (len_lt xt yt)
     end))


let rec len_eq : Prims.list<ch>  ->  Prims.list<ch>  ->  Prims.bool = (fun ( x  :  Prims.list<ch> ) ( y  :  Prims.list<ch> ) -> (match (((x), (y))) with
| ([], []) -> begin
     true
     end
| ([], (uu___)::uu___1) -> begin
     false
     end
| ((uu___)::uu___1, []) -> begin
     false
     end
| ((uu___)::xt, (uu___1)::yt) -> begin
     (len_eq xt yt)
     end))


let rec mem_ch : ch  ->  Prims.list<ch>  ->  Prims.bool = (fun ( c  :  ch ) ( l  :  Prims.list<ch> ) -> (match (l) with
| [] -> begin
     false
     end
| (x)::t -> begin
     ((Prims.op_Equals x c) || (mem_ch c t))
     end))


let digits_asc : Prims.list<ch> = (CD0)::(CD1)::(CD2)::(CD3)::(CD4)::(CD5)::(CD6)::(CD7)::(CD8)::(CD9)::[]


let rec after : ch  ->  Prims.list<ch>  ->  Prims.list<ch> = (fun ( c  :  ch ) ( l  :  Prims.list<ch> ) -> (match (l) with
| [] -> begin
     []
     end
| (x)::t -> begin
      
if (Prims.op_Equals x c) then begin
     t
     end else begin
     (after c t)
     end
     end))


let digit_lt : ch  ->  ch  ->  Prims.bool = (fun ( a  :  ch ) ( b  :  ch ) -> (mem_ch b (after a digits_asc)))


let rec dig_le : Prims.list<ch>  ->  Prims.list<ch>  ->  Prims.bool = (fun ( x  :  Prims.list<ch> ) ( y  :  Prims.list<ch> ) -> (match (((x), (y))) with
| ([], []) -> begin
     true
     end
| ((a)::xt, (b)::yt) -> begin
      
if (Prims.op_Equals a b) then begin
     (dig_le xt yt)
     end else begin
     (digit_lt a b)
     end
     end
| uu___ -> begin
     false
     end))


let rec strip0 : Prims.list<ch>  ->  Prims.list<ch> = (fun ( d  :  Prims.list<ch> ) -> (match (d) with
| (CD0)::t -> begin
      
if (match (t) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end) then begin
     d
     end else begin
     (strip0 t)
     end
     end
| uu___ -> begin
     d
     end))


let lim_i53 : Prims.list<ch> = (CD9)::(CD0)::(CD0)::(CD7)::(CD1)::(CD9)::(CD9)::(CD2)::(CD5)::(CD4)::(CD7)::(CD4)::(CD0)::(CD9)::(CD9)::(CD2)::[]


let lim_i32_pos : Prims.list<ch> = (CD2)::(CD1)::(CD4)::(CD7)::(CD4)::(CD8)::(CD3)::(CD6)::(CD4)::(CD7)::[]


let lim_i32_neg : Prims.list<ch> = (CD2)::(CD1)::(CD4)::(CD7)::(CD4)::(CD8)::(CD3)::(CD6)::(CD4)::(CD8)::[]


let int53_safe : Prims.list<ch>  ->  Prims.bool = (fun ( d  :  Prims.list<ch> ) -> ((len_lt d lim_i53) || ((len_eq d lim_i53) && (dig_le d lim_i53))))


let int53_safe_value : Prims.list<ch>  ->  Prims.bool = (fun ( d  :  Prims.list<ch> ) -> (int53_safe (strip0 d)))


let int32_fits : Prims.bool  ->  Prims.list<ch>  ->  Prims.bool = (fun ( neg  :  Prims.bool ) ( d  :  Prims.list<ch> ) -> (

let s = (strip0 d)
in (

let lim =  
if neg then begin
     lim_i32_neg
     end else begin
     lim_i32_pos
     end
in ((not ((match (s) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end))) && ((len_lt s lim) || ((len_eq s lim) && (dig_le s lim)))))))


let str_kind : ekind  ->  Prims.bool = (fun ( k  :  ekind ) -> ((((((Prims.op_Equals k ExpectedToken) || (Prims.op_Equals k UnterminatedString)) || (Prims.op_Equals k UnterminatedEscape)) || (Prims.op_Equals k TruncatedUnicodeEscape)) || (Prims.op_Equals k BadEscape)) || (Prims.op_Equals k BadHexDigit)))


let rec skip_ws : Prims.list<ch>  ->  Prims.list<ch> = (fun ( s  :  Prims.list<ch> ) -> (match (s) with
| [] -> begin
     []
     end
| (c)::t -> begin
      
if (is_ws c) then begin
     (skip_ws t)
     end else begin
     s
     end
     end))


let expect : ch  ->  Prims.list<ch>  ->  pres<unit> = (fun ( c  :  ch ) ( s  :  Prims.list<ch> ) -> (match (s) with
| (x)::t -> begin
      
if (Prims.op_Equals x c) then begin
     POk ((), t)
     end else begin
     PErr (ExpectedToken, (msg_expect_char c), s)
     end
     end
| [] -> begin
     PErr (ExpectedToken, (msg_expect_char c), s)
     end))


let rec string_body : Prims.list<och>  ->  Prims.list<ch>  ->  pres<Prims.list<och>> = (fun ( acc  :  Prims.list<och> ) ( s  :  Prims.list<ch> ) -> (match (s) with
| [] -> begin
     PErr (UnterminatedString, "unterminated string", [])
     end
| (CQuote)::t -> begin
     POk ((rev acc), t)
     end
| (CBackslash)::t -> begin
     (match (t) with
| [] -> begin
     PErr (UnterminatedEscape, "unterminated escape", [])
     end
| (CLu)::u -> begin
     (match (u) with
| (a)::(b)::(c)::(d)::r -> begin
      
if ((((is_hex a) && (is_hex b)) && (is_hex c)) && (is_hex d)) then begin
     (string_body ((OUni (a, b, c, d))::acc) r)
     end else begin
     PErr (BadHexDigit, "bad hex digit in \\u escape", u)
     end
     end
| uu___ -> begin
     PErr (TruncatedUnicodeEscape, "truncated \\u escape", u)
     end)
     end
| (e)::u -> begin
      
if (is_short_escape e) then begin
     (string_body ((OEsc (e))::acc) u)
     end else begin
     PErr (BadEscape, (msg_bad_escape e), u)
     end
     end)
     end
| (c)::t -> begin
     (string_body ((OLit (c))::acc) t)
     end))


let parse_string : Prims.list<ch>  ->  pres<Prims.list<och>> = (fun ( s  :  Prims.list<ch> ) -> (match ((expect CQuote s)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (uu___, t) -> begin
     (string_body [] t)
     end))


let rec take_digits : Prims.list<ch>  ->  (Prims.list<ch> * Prims.list<ch>) = (fun ( s  :  Prims.list<ch> ) -> (match (s) with
| (d)::t -> begin
      
if (is_digit d) then begin
     (

let uu___ = (take_digits t)
in (match (uu___) with
| (ds, r) -> begin
     (((d)::ds), (r))
     end))
     end else begin
     (([]), (s))
     end
     end
| [] -> begin
     (([]), (s))
     end))


let rec app = (fun ( x  :  Prims.list<'a> ) ( y  :  Prims.list<'a> ) -> (match (x) with
| [] -> begin
     y
     end
| (h)::t -> begin
     (h)::(app t y)
     end))


let scan_number : Prims.list<ch>  ->  (Prims.list<ch> * Prims.bool * Prims.list<ch>) = (fun ( s  :  Prims.list<ch> ) -> (

let uu___ = (match (s) with
| (CMinus)::t -> begin
     (((CMinus)::[]), (t))
     end
| uu___1 -> begin
     (([]), (s))
     end)
in (match (uu___) with
| (neg, s1) -> begin
     (

let uu___1 = (take_digits s1)
in (match (uu___1) with
| (ints, s2) -> begin
     (

let uu___2 = (match (s2) with
| (CDot)::t -> begin
     (

let uu___3 = (take_digits t)
in (match (uu___3) with
| (fs, r) -> begin
     (((CDot)::fs), (true), (r))
     end))
     end
| uu___3 -> begin
     (([]), (false), (s2))
     end)
in (match (uu___2) with
| (frac, isf1, s3) -> begin
     (

let uu___3 = (match (s3) with
| (e)::t -> begin
      
if (is_exp e) then begin
     (match (t) with
| (sg)::u -> begin
      
if ((Prims.op_Equals sg CPlus) || (Prims.op_Equals sg CMinus)) then begin
     (

let uu___4 = (take_digits u)
in (match (uu___4) with
| (ds, r) -> begin
     (((e)::(sg)::ds), (true), (r))
     end))
     end else begin
     (

let uu___4 = (take_digits t)
in (match (uu___4) with
| (ds, r) -> begin
     (((e)::ds), (true), (r))
     end))
     end
     end
| [] -> begin
     (((e)::[]), (true), ([]))
     end)
     end else begin
     (([]), (false), (s3))
     end
     end
| [] -> begin
     (([]), (false), (s3))
     end)
in (match (uu___3) with
| (expo, isf2, s4) -> begin
     (((app neg (app ints (app frac expo)))), ((isf1 || isf2)), (s4))
     end))
     end))
     end))
     end)))

type freadv =
| FFinite
| FNonFinite
| FUnparsable


let uu___is_FFinite : freadv  ->  Prims.bool = (fun ( projectee  :  freadv ) -> (match (projectee) with
| FFinite -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_FNonFinite : freadv  ->  Prims.bool = (fun ( projectee  :  freadv ) -> (match (projectee) with
| FNonFinite -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_FUnparsable : freadv  ->  Prims.bool = (fun ( projectee  :  freadv ) -> (match (projectee) with
| FUnparsable -> begin
     true
     end
| uu___ -> begin
     false
     end))


let classify_number : (Prims.list<ch>  ->  freadv)  ->  Prims.list<ch>  ->  Prims.bool  ->  pres<jval> = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( tok  :  Prims.list<ch> ) ( isf  :  Prims.bool ) -> (

let uu___ = (match (tok) with
| (CMinus)::t -> begin
     ((true), (t))
     end
| uu___1 -> begin
     ((false), (tok))
     end)
in (match (uu___) with
| (neg, digits) -> begin
      
if isf then begin
     (match ((float_read tok)) with
| FFinite -> begin
     POk (JFloat (tok), [])
     end
| FNonFinite -> begin
     PErr (MalformedNumber, (msg_nonfinite tok), [])
     end
| FUnparsable -> begin
     PErr (MalformedNumber, (msg_malformed tok), [])
     end)
     end else begin
      
if (int32_fits neg digits) then begin
     POk (JInt (tok), [])
     end else begin
      
if (int53_safe digits) then begin
     (match ((float_read tok)) with
| FUnparsable -> begin
     PErr (MalformedNumber, (msg_malformed tok), [])
     end
| uu___1 -> begin
     POk (JFloat (tok), [])
     end)
     end else begin
     PErr (MalformedNumber, (msg_int53 tok), [])
     end
     end
     end
     end)))


let parse_number : (Prims.list<ch>  ->  freadv)  ->  Prims.list<ch>  ->  pres<jval> = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( s  :  Prims.list<ch> ) -> (

let uu___ = (scan_number s)
in (match (uu___) with
| (tok, isf, rest) -> begin
     (match ((classify_number float_read tok isf)) with
| POk (v, uu___1) -> begin
     POk (v, rest)
     end
| PErr (k, m, uu___1) -> begin
     PErr (k, m, rest)
     end)
     end)))


let starts_container : Prims.list<ch>  ->  Prims.bool = (fun ( s  :  Prims.list<ch> ) -> (match (s) with
| (CLBrace)::uu___ -> begin
     true
     end
| (CLBrack)::uu___ -> begin
     true
     end
| uu___ -> begin
     false
     end))


let val_kind : ekind  ->  Prims.bool = (fun ( k  :  ekind ) -> (Prims.op_Less_Greater k TrailingCharacters))


let depth_confined = (fun ( r  :  pres<'a> ) -> (match (r) with
| PErr (k, uu___, at) -> begin
     ((val_kind k) && ((Prims.op_Less_Greater k MaxDepthExceeded) || (starts_container at)))
     end
| POk (uu___, uu___1) -> begin
     true
     end))


let drop_null4 : Prims.list<ch>  ->  FStar_Pervasives_Native.option<Prims.list<ch>> = (fun ( s  :  Prims.list<ch> ) -> (match (s) with
| (CLn)::(CLu)::(CLl)::(CLl)::t -> begin
     FStar_Pervasives_Native.Some (t)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let rec parse_value : (Prims.list<ch>  ->  freadv)  ->  Prims.string  ->  Prims.bool  ->  Prims.list<unit>  ->  Prims.list<ch>  ->  pres<jval> = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( cap  :  Prims.string ) ( tol  :  Prims.bool ) ( b  :  Prims.list<unit> ) ( s  :  Prims.list<ch> ) -> (

let w = (skip_ws s)
in (match (w) with
| [] -> begin
     PErr (UnexpectedEndOfInput, "unexpected end of input", [])
     end
| (CQuote)::uu___ -> begin
     (match ((parse_string w)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (cs, r) -> begin
     POk (JStr (cs), r)
     end)
     end
| (CLBrace)::uu___ -> begin
     (parse_object float_read cap tol b w)
     end
| (CLBrack)::uu___ -> begin
     (parse_array float_read cap tol b w)
     end
| (CLt)::(CLr)::(CLu)::(CLe)::t -> begin
     POk (JBool (true), t)
     end
| (CLt)::uu___ -> begin
     PErr (ExpectedToken, (msg_expect_lit "true"), w)
     end
| (CLf)::(CLa)::(CLl)::(CLs)::(CLe)::t -> begin
     POk (JBool (false), t)
     end
| (CLf)::uu___ -> begin
     PErr (ExpectedToken, (msg_expect_lit "false"), w)
     end
| (CLn)::uu___ -> begin
     PErr (NullNotRepresentable,  
if tol then begin
     msg_null_tolerant
     end else begin
     msg_null_strict
     end, w)
     end
| (c)::uu___ -> begin
      
if ((Prims.op_Equals c CMinus) || (is_digit c)) then begin
     (match ((parse_number float_read w)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (v, r) -> begin
     POk (v, r)
     end)
     end else begin
     PErr (UnexpectedChar, (msg_unexpected_char c), w)
     end
     end)))
and parse_object : (Prims.list<ch>  ->  freadv)  ->  Prims.string  ->  Prims.bool  ->  Prims.list<unit>  ->  Prims.list<ch>  ->  pres<jval> = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( cap  :  Prims.string ) ( tol  :  Prims.bool ) ( b  :  Prims.list<unit> ) ( w  :  Prims.list<ch> ) -> (match (b) with
| [] -> begin
     PErr (MaxDepthExceeded, (msg_depth cap), w)
     end
| (uu___)::b' -> begin
     (match ((expect CLBrace w)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (uu___1, t) -> begin
     (

let t1 = (skip_ws t)
in (match (t1) with
| (CRBrace)::t2 -> begin
     POk (JObj ([]), t2)
     end
| uu___2 -> begin
     (match ((parse_members float_read cap tol b' [] t1)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (fs, r) -> begin
     POk (JObj (fs), r)
     end)
     end))
     end)
     end))
and parse_array : (Prims.list<ch>  ->  freadv)  ->  Prims.string  ->  Prims.bool  ->  Prims.list<unit>  ->  Prims.list<ch>  ->  pres<jval> = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( cap  :  Prims.string ) ( tol  :  Prims.bool ) ( b  :  Prims.list<unit> ) ( w  :  Prims.list<ch> ) -> (match (b) with
| [] -> begin
     PErr (MaxDepthExceeded, (msg_depth cap), w)
     end
| (uu___)::b' -> begin
     (match ((expect CLBrack w)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (uu___1, t) -> begin
     (

let t1 = (skip_ws t)
in (match (t1) with
| (CRBrack)::t2 -> begin
     POk (JArr ([]), t2)
     end
| uu___2 -> begin
     (match ((parse_items float_read cap tol b' [] t1)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (xs, r) -> begin
     POk (JArr (xs), r)
     end)
     end))
     end)
     end))
and parse_members : (Prims.list<ch>  ->  freadv)  ->  Prims.string  ->  Prims.bool  ->  Prims.list<unit>  ->  Prims.list<(Prims.list<och> * jval)>  ->  Prims.list<ch>  ->  pres<Prims.list<(Prims.list<och> * jval)>> = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( cap  :  Prims.string ) ( tol  :  Prims.bool ) ( b  :  Prims.list<unit> ) ( acc  :  Prims.list<(Prims.list<och> * jval)> ) ( s  :  Prims.list<ch> ) -> (

let w = (skip_ws s)
in (match ((parse_string w)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (key, t) -> begin
     (

let t1 = (skip_ws t)
in (match ((expect CColon t1)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (uu___, t2) -> begin
     (

let t3 = (skip_ws t2)
in (

let erased =  
if tol then begin
     (drop_null4 t3)
     end else begin
     FStar_Pervasives_Native.None
     end
in (match (erased) with
| FStar_Pervasives_Native.Some (t4) -> begin
     (

let t5 = (skip_ws t4)
in (match (t5) with
| (CComma)::t6 -> begin
     (parse_members float_read cap tol b acc t6)
     end
| (CRBrace)::t6 -> begin
     POk ((rev acc), t6)
     end
| uu___1 -> begin
     PErr (ExpectedToken, "expected \',\' or \'}\'", t5)
     end))
     end
| FStar_Pervasives_Native.None -> begin
     (match ((parse_value float_read cap tol b t3)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (v, t4) -> begin
     (

let t5 = (skip_ws t4)
in (match (t5) with
| (CComma)::t6 -> begin
     (parse_members float_read cap tol b ((((key), (v)))::acc) t6)
     end
| (CRBrace)::t6 -> begin
     POk ((rev ((((key), (v)))::acc)), t6)
     end
| uu___1 -> begin
     PErr (ExpectedToken, "expected \',\' or \'}\'", t5)
     end))
     end)
     end)))
     end))
     end)))
and parse_items : (Prims.list<ch>  ->  freadv)  ->  Prims.string  ->  Prims.bool  ->  Prims.list<unit>  ->  Prims.list<jval>  ->  Prims.list<ch>  ->  pres<Prims.list<jval>> = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( cap  :  Prims.string ) ( tol  :  Prims.bool ) ( b  :  Prims.list<unit> ) ( acc  :  Prims.list<jval> ) ( s  :  Prims.list<ch> ) -> (match ((parse_value float_read cap tol b s)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (v, t) -> begin
     (

let t1 = (skip_ws t)
in (match (t1) with
| (CComma)::t2 -> begin
     (parse_items float_read cap tol b ((v)::acc) t2)
     end
| (CRBrack)::t2 -> begin
     POk ((rev ((v)::acc)), t2)
     end
| uu___ -> begin
     PErr (ExpectedToken, "expected \',\' or \']\'", t1)
     end))
     end))


let parse : (Prims.list<ch>  ->  freadv)  ->  Prims.string  ->  policy  ->  Prims.list<unit>  ->  Prims.list<ch>  ->  jresult = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( cap  :  Prims.string ) ( pol  :  policy ) ( b  :  Prims.list<unit> ) ( s  :  Prims.list<ch> ) -> (

let tol = (match (pol) with
| EraseMemberNull -> begin
     true
     end
| uu___ -> begin
     false
     end)
in (match ((parse_value float_read cap tol b s)) with
| PErr (k, m, a) -> begin
     RErr (k, m, a)
     end
| POk (v, r) -> begin
     (

let r1 = (skip_ws r)
in  
if (match (r1) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end) then begin
     ROk (v)
     end else begin
     RErr (TrailingCharacters, "trailing characters", r1)
     end)
     end)))




