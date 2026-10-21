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
| CHiSur of Prims.string
| CLoSur of Prims.string
| CCtrl of Prims.string * Prims.string


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


let uu___is_CHiSur : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CHiSur (c) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__CHiSur__item__c : ch  ->  Prims.string = (fun ( projectee  :  ch ) -> (match (projectee) with
| CHiSur (c) -> begin
     c
     end))


let uu___is_CLoSur : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CLoSur (c) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__CLoSur__item__c : ch  ->  Prims.string = (fun ( projectee  :  ch ) -> (match (projectee) with
| CLoSur (c) -> begin
     c
     end))


let uu___is_CCtrl : ch  ->  Prims.bool = (fun ( projectee  :  ch ) -> (match (projectee) with
| CCtrl (c, code) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__CCtrl__item__c : ch  ->  Prims.string = (fun ( projectee  :  ch ) -> (match (projectee) with
| CCtrl (c, code) -> begin
     c
     end))


let __proj__CCtrl__item__code : ch  ->  Prims.string = (fun ( projectee  :  ch ) -> (match (projectee) with
| CCtrl (c, code) -> begin
     code
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
     end
| CHiSur (s) -> begin
     s
     end
| CLoSur (s) -> begin
     s
     end
| CCtrl (s, uu___) -> begin
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


let is_ctrl : ch  ->  Prims.bool = (fun ( c  :  ch ) -> ((((Prims.op_Equals c CTab) || (Prims.op_Equals c CNewline)) || (Prims.op_Equals c CReturn)) || (match (c) with
| CCtrl (c1, code) -> begin
     true
     end
| uu___ -> begin
     false
     end)))


let ctrl_code : ch  ->  Prims.string = (fun ( c  :  ch ) -> (match (c) with
| CTab -> begin
     "U+0009"
     end
| CNewline -> begin
     "U+000A"
     end
| CReturn -> begin
     "U+000D"
     end
| CCtrl (uu___, code) -> begin
     code
     end
| uu___ -> begin
     ""
     end))


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

type limit =
| LDepth
| LString
| LItems
| LMembers
| LBytes


let uu___is_LDepth : limit  ->  Prims.bool = (fun ( projectee  :  limit ) -> (match (projectee) with
| LDepth -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_LString : limit  ->  Prims.bool = (fun ( projectee  :  limit ) -> (match (projectee) with
| LString -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_LItems : limit  ->  Prims.bool = (fun ( projectee  :  limit ) -> (match (projectee) with
| LItems -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_LMembers : limit  ->  Prims.bool = (fun ( projectee  :  limit ) -> (match (projectee) with
| LMembers -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_LBytes : limit  ->  Prims.bool = (fun ( projectee  :  limit ) -> (match (projectee) with
| LBytes -> begin
     true
     end
| uu___ -> begin
     false
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
| DuplicateMember
| LimitExceeded of limit


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


let uu___is_DuplicateMember : ekind  ->  Prims.bool = (fun ( projectee  :  ekind ) -> (match (projectee) with
| DuplicateMember -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_LimitExceeded : ekind  ->  Prims.bool = (fun ( projectee  :  ekind ) -> (match (projectee) with
| LimitExceeded (l) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__LimitExceeded__item__l : ekind  ->  limit = (fun ( projectee  :  ekind ) -> (match (projectee) with
| LimitExceeded (l) -> begin
     l
     end))

type bound =
| Unbounded
| Bounded of Prims.list<unit>


let uu___is_Unbounded : bound  ->  Prims.bool = (fun ( projectee  :  bound ) -> (match (projectee) with
| Unbounded -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_Bounded : bound  ->  Prims.bool = (fun ( projectee  :  bound ) -> (match (projectee) with
| Bounded (m) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__Bounded__item__m : bound  ->  Prims.list<unit> = (fun ( projectee  :  bound ) -> (match (projectee) with
| Bounded (m) -> begin
     m
     end))

type lims = {lstr : bound; litems : bound; lmembers : bound}


let __proj__Mklims__item__lstr : lims  ->  bound = (fun ( projectee  :  lims ) -> (match (projectee) with
| {lstr = lstr; litems = litems; lmembers = lmembers} -> begin
     lstr
     end))


let __proj__Mklims__item__litems : lims  ->  bound = (fun ( projectee  :  lims ) -> (match (projectee) with
| {lstr = lstr; litems = litems; lmembers = lmembers} -> begin
     litems
     end))


let __proj__Mklims__item__lmembers : lims  ->  bound = (fun ( projectee  :  lims ) -> (match (projectee) with
| {lstr = lstr; litems = litems; lmembers = lmembers} -> begin
     lmembers
     end))


let unbounded : lims = {lstr = Unbounded; litems = Unbounded; lmembers = Unbounded}


let rec spent_by = (fun ( m  :  Prims.list<unit> ) ( acc  :  Prims.list<'a> ) -> (match (((m), (acc))) with
| ([], uu___) -> begin
     true
     end
| (uu___, []) -> begin
     false
     end
| ((uu___)::mt, (uu___1)::at) -> begin
     (spent_by mt at)
     end))


let spent = (fun ( bd  :  bound ) ( acc  :  Prims.list<'a> ) -> (match (bd) with
| Unbounded -> begin
     false
     end
| Bounded (m) -> begin
     (spent_by m acc)
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
| RLimit of limit * Prims.list<ch>


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


let uu___is_RLimit : jresult  ->  Prims.bool = (fun ( projectee  :  jresult ) -> (match (projectee) with
| RLimit (l, at) -> begin
     true
     end
| uu___ -> begin
     false
     end))


let __proj__RLimit__item__l : jresult  ->  limit = (fun ( projectee  :  jresult ) -> (match (projectee) with
| RLimit (l, at) -> begin
     l
     end))


let __proj__RLimit__item__at : jresult  ->  Prims.list<ch> = (fun ( projectee  :  jresult ) -> (match (projectee) with
| RLimit (l, at) -> begin
     at
     end))


let msg_expect_char : ch  ->  Prims.string = (fun ( c  :  ch ) -> (Prims.strcat "expected \'" (Prims.strcat (ch_str c) "\'")))


let msg_expect_lit : Prims.string  ->  Prims.string = (fun ( l  :  Prims.string ) -> (Prims.strcat "expected \'" (Prims.strcat l "\'")))


let msg_unexpected_char : ch  ->  Prims.string = (fun ( c  :  ch ) -> (Prims.strcat "unexpected character \'" (Prims.strcat (ch_str c) "\'")))


let msg_bad_escape : ch  ->  Prims.string = (fun ( e  :  ch ) -> (Prims.strcat "bad escape \'\\" (Prims.strcat (ch_str e) "\'")))


let msg_malformed : Prims.list<ch>  ->  Prims.string = (fun ( tok  :  Prims.list<ch> ) -> (Prims.strcat "malformed number: " (chs_str tok)))


let msg_lone_high : Prims.string = "ill-formed string: a high surrogate not followed by a low surrogate"


let msg_lone_low : Prims.string = "ill-formed string: a low surrogate with no high surrogate before it"


let msg_nonfinite : Prims.list<ch>  ->  Prims.string = (fun ( tok  :  Prims.list<ch> ) -> (Prims.strcat "number outside the finite double range; it cannot round-trip on the wire: " (chs_str tok)))


let msg_int53 : Prims.list<ch>  ->  Prims.string = (fun ( tok  :  Prims.list<ch> ) -> (Prims.strcat "integer literal outside the int53 safe range (|n| > 2^53); it cannot round-trip without precision loss: " (chs_str tok)))


let msg_null_strict : Prims.string = "null is not representable in the Fuaran wire JVal model"


let msg_ctrl : ch  ->  Prims.string = (fun ( c  :  ch ) -> (Prims.strcat "unescaped control character " (Prims.strcat (ctrl_code c) " in a string")))


let msg_duplicate : Prims.string = "repeated member name in one object"


let msg_null_tolerant : Prims.string = "null is not representable in the Fuaran wire JVal model, and this position has no absence to erase it to (only an object-member null is erased)"


let msg_depth : Prims.string  ->  Prims.string = (fun ( rendered_cap  :  Prims.string ) -> (Prims.strcat "max nesting depth " (Prims.strcat rendered_cap " exceeded")))


let msg_limit : Prims.string = ""


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


let str_kind : ekind  ->  Prims.bool = (fun ( k  :  ekind ) -> (((((((Prims.op_Equals k ExpectedToken) || (Prims.op_Equals k UnterminatedString)) || (Prims.op_Equals k UnterminatedEscape)) || (Prims.op_Equals k TruncatedUnicodeEscape)) || (Prims.op_Equals k BadEscape)) || (Prims.op_Equals k BadHexDigit)) || (Prims.op_Equals k (LimitExceeded (LString)))))


let str_confined = (fun ( lstr  :  bound ) ( r  :  pres<'a> ) -> (match (r) with
| PErr (LimitExceeded (LString), uu___, uu___1) -> begin
     (match (lstr) with
| Bounded (m) -> begin
     true
     end
| uu___2 -> begin
     false
     end)
     end
| uu___ -> begin
     true
     end))


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

type sclass =
| SNone
| SHigh
| SLow


let uu___is_SNone : sclass  ->  Prims.bool = (fun ( projectee  :  sclass ) -> (match (projectee) with
| SNone -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_SHigh : sclass  ->  Prims.bool = (fun ( projectee  :  sclass ) -> (match (projectee) with
| SHigh -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_SLow : sclass  ->  Prims.bool = (fun ( projectee  :  sclass ) -> (match (projectee) with
| SLow -> begin
     true
     end
| uu___ -> begin
     false
     end))


let lit_class : ch  ->  sclass = (fun ( c  :  ch ) -> (match (c) with
| CHiSur (uu___) -> begin
     SHigh
     end
| CLoSur (uu___) -> begin
     SLow
     end
| uu___ -> begin
     SNone
     end))


let uni_class : ch  ->  ch  ->  sclass = (fun ( a  :  ch ) ( b  :  ch ) ->  
if ((Prims.op_Equals a CLd) || (Prims.op_Equals a CUd)) then begin
      
if ((((((Prims.op_Equals b CD8) || (Prims.op_Equals b CD9)) || (Prims.op_Equals b CLa)) || (Prims.op_Equals b CLb)) || (Prims.op_Equals b CUa)) || (Prims.op_Equals b CUb)) then begin
     SHigh
     end else begin
      
if ((((((((Prims.op_Equals b CLc) || (Prims.op_Equals b CLd)) || (Prims.op_Equals b CLe)) || (Prims.op_Equals b CLf)) || (Prims.op_Equals b CUc)) || (Prims.op_Equals b CUd)) || (Prims.op_Equals b CUe)) || (Prims.op_Equals b CUf)) then begin
     SLow
     end else begin
     SNone
     end
     end
     end else begin
     SNone
     end)


let och_class : och  ->  sclass = (fun ( o  :  och ) -> (match (o) with
| OLit (c) -> begin
     (lit_class c)
     end
| OEsc (uu___) -> begin
     SNone
     end
| OUni (a, b, uu___, uu___1) -> begin
     (uni_class a b)
     end))


let sur_fault : Prims.bool  ->  sclass  ->  FStar_Pervasives_Native.option<Prims.string> = (fun ( pending  :  Prims.bool ) ( cls  :  sclass ) ->  
if (pending && (Prims.op_Less_Greater cls SLow)) then begin
     FStar_Pervasives_Native.Some (msg_lone_high)
     end else begin
      
if ((not (pending)) && (Prims.op_Equals cls SLow)) then begin
     FStar_Pervasives_Native.Some (msg_lone_low)
     end else begin
     FStar_Pervasives_Native.None
     end
     end)


let rec string_body : bound  ->  Prims.list<unit>  ->  Prims.bool  ->  Prims.list<och>  ->  Prims.list<ch>  ->  pres<Prims.list<och>> = (fun ( lstr  :  bound ) ( n  :  Prims.list<unit> ) ( pending  :  Prims.bool ) ( acc  :  Prims.list<och> ) ( s  :  Prims.list<ch> ) -> (

let n' =  
if pending then begin
     n
     end else begin
     (())::n
     end
in (

let over = ((not (pending)) && (spent lstr n))
in (match (s) with
| [] -> begin
     PErr (UnterminatedString, "unterminated string", [])
     end
| (CQuote)::t -> begin
      
if pending then begin
     PErr (BadEscape, msg_lone_high, t)
     end else begin
     POk ((rev acc), t)
     end
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
     (match ((sur_fault pending (uni_class a b))) with
| FStar_Pervasives_Native.Some (m) -> begin
     PErr (BadEscape, m, r)
     end
| FStar_Pervasives_Native.None -> begin
      
if over then begin
     PErr (LimitExceeded (LString), msg_limit, r)
     end else begin
     (string_body lstr n' (Prims.op_Equals (uni_class a b) SHigh) ((OUni (a, b, c, d))::acc) r)
     end
     end)
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
     (match ((sur_fault pending SNone)) with
| FStar_Pervasives_Native.Some (m) -> begin
     PErr (BadEscape, m, u)
     end
| FStar_Pervasives_Native.None -> begin
      
if over then begin
     PErr (LimitExceeded (LString), msg_limit, u)
     end else begin
     (string_body lstr n' false ((OEsc (e))::acc) u)
     end
     end)
     end else begin
     PErr (BadEscape, (msg_bad_escape e), u)
     end
     end)
     end
| (c)::t -> begin
      
if (is_ctrl c) then begin
     PErr (BadEscape, (msg_ctrl c), s)
     end else begin
     (match ((sur_fault pending (lit_class c))) with
| FStar_Pervasives_Native.Some (m) -> begin
     PErr (BadEscape, m, t)
     end
| FStar_Pervasives_Native.None -> begin
      
if over then begin
     PErr (LimitExceeded (LString), msg_limit, t)
     end else begin
     (string_body lstr n' (Prims.op_Equals (lit_class c) SHigh) ((OLit (c))::acc) t)
     end
     end)
     end
     end))))


let parse_string : bound  ->  Prims.list<ch>  ->  pres<Prims.list<och>> = (fun ( lstr  :  bound ) ( s  :  Prims.list<ch> ) -> (match ((expect CQuote s)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (uu___, t) -> begin
     (string_body lstr [] false [] t)
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
     (((ListAlgebra.app neg (ListAlgebra.app ints (ListAlgebra.app frac expo)))), ((isf1 || isf2)), (s4))
     end))
     end))
     end))
     end)))

type freadv =
| FFinite
| FNonFinite
| FUnparsable
| FCanonical


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


let uu___is_FCanonical : freadv  ->  Prims.bool = (fun ( projectee  :  freadv ) -> (match (projectee) with
| FCanonical -> begin
     true
     end
| uu___ -> begin
     false
     end))


let after_int : Prims.list<ch>  ->  FStar_Pervasives_Native.option<Prims.list<ch>> = (fun ( s  :  Prims.list<ch> ) -> (match (s) with
| (CD0)::t -> begin
     FStar_Pervasives_Native.Some (t)
     end
| (d)::uu___ -> begin
      
if (is_digit d) then begin
     FStar_Pervasives_Native.Some ((FStar_Pervasives_Native.snd (take_digits s)))
     end else begin
     FStar_Pervasives_Native.None
     end
     end
| [] -> begin
     FStar_Pervasives_Native.None
     end))


let after_frac : Prims.list<ch>  ->  FStar_Pervasives_Native.option<Prims.list<ch>> = (fun ( s  :  Prims.list<ch> ) -> (match (s) with
| (CDot)::t -> begin
     (

let uu___ = (take_digits t)
in (match (uu___) with
| (ds, r) -> begin
      
if (match (ds) with
| (hd)::tl -> begin
     true
     end
| uu___1 -> begin
     false
     end) then begin
     FStar_Pervasives_Native.Some (r)
     end else begin
     FStar_Pervasives_Native.None
     end
     end))
     end
| uu___ -> begin
     FStar_Pervasives_Native.Some (s)
     end))


let after_exp : Prims.list<ch>  ->  FStar_Pervasives_Native.option<Prims.list<ch>> = (fun ( s  :  Prims.list<ch> ) -> (match (s) with
| (e)::t -> begin
      
if (is_exp e) then begin
     (

let u = (match (t) with
| (sg)::v -> begin
      
if ((Prims.op_Equals sg CPlus) || (Prims.op_Equals sg CMinus)) then begin
     v
     end else begin
     t
     end
     end
| [] -> begin
     t
     end)
in (

let uu___ = (take_digits u)
in (match (uu___) with
| (ds, r) -> begin
      
if (match (ds) with
| (hd)::tl -> begin
     true
     end
| uu___1 -> begin
     false
     end) then begin
     FStar_Pervasives_Native.Some (r)
     end else begin
     FStar_Pervasives_Native.None
     end
     end)))
     end else begin
     FStar_Pervasives_Native.Some (s)
     end
     end
| [] -> begin
     FStar_Pervasives_Native.Some (s)
     end))


let is_json_number : Prims.list<ch>  ->  Prims.bool = (fun ( tok  :  Prims.list<ch> ) -> (

let start = (match (tok) with
| (CMinus)::t -> begin
     t
     end
| uu___ -> begin
     tok
     end)
in (match ((after_int start)) with
| FStar_Pervasives_Native.None -> begin
     false
     end
| FStar_Pervasives_Native.Some (s1) -> begin
     (match ((after_frac s1)) with
| FStar_Pervasives_Native.None -> begin
     false
     end
| FStar_Pervasives_Native.Some (s2) -> begin
     (match ((after_exp s2)) with
| FStar_Pervasives_Native.None -> begin
     false
     end
| FStar_Pervasives_Native.Some (s3) -> begin
     (match (s3) with
| [] -> begin
     true
     end
| uu___ -> begin
     false
     end)
     end)
     end)
     end)))


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
      
if (not ((is_json_number tok))) then begin
     PErr (MalformedNumber, (msg_malformed tok), [])
     end else begin
      
if isf then begin
     (match ((float_read tok)) with
| FFinite -> begin
     POk (JFloat (tok), [])
     end
| FCanonical -> begin
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
     (match ((float_read tok)) with
| FCanonical -> begin
     POk (JFloat (tok), [])
     end
| uu___1 -> begin
     PErr (MalformedNumber, (msg_int53 tok), [])
     end)
     end
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


let bounds_confined = (fun ( lim  :  lims ) ( r  :  pres<'a> ) -> (match (r) with
| PErr (LimitExceeded (LString), uu___, uu___1) -> begin
     (match (lim.lstr) with
| Bounded (m) -> begin
     true
     end
| uu___2 -> begin
     false
     end)
     end
| PErr (LimitExceeded (LItems), uu___, uu___1) -> begin
     (match (lim.litems) with
| Bounded (m) -> begin
     true
     end
| uu___2 -> begin
     false
     end)
     end
| PErr (LimitExceeded (LMembers), uu___, uu___1) -> begin
     (match (lim.lmembers) with
| Bounded (m) -> begin
     true
     end
| uu___2 -> begin
     false
     end)
     end
| PErr (LimitExceeded (LDepth), uu___, uu___1) -> begin
     false
     end
| PErr (LimitExceeded (LBytes), uu___, uu___1) -> begin
     false
     end
| uu___ -> begin
     true
     end))


let drop_null4 : Prims.list<ch>  ->  FStar_Pervasives_Native.option<Prims.list<ch>> = (fun ( s  :  Prims.list<ch> ) -> (match (s) with
| (CLn)::(CLu)::(CLl)::(CLl)::t -> begin
     FStar_Pervasives_Native.Some (t)
     end
| uu___ -> begin
     FStar_Pervasives_Native.None
     end))


let rec has_key : Prims.list<och>  ->  Prims.list<(Prims.list<och> * jval)>  ->  Prims.bool = (fun ( k  :  Prims.list<och> ) ( fs  :  Prims.list<(Prims.list<och> * jval)> ) -> (match (fs) with
| [] -> begin
     false
     end
| ((k', uu___))::t -> begin
     ((Prims.op_Equals k k') || (has_key k t))
     end))


let rec parse_value : (Prims.list<ch>  ->  freadv)  ->  Prims.string  ->  lims  ->  Prims.bool  ->  Prims.list<unit>  ->  Prims.list<ch>  ->  pres<jval> = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( cap  :  Prims.string ) ( lim  :  lims ) ( tol  :  Prims.bool ) ( b  :  Prims.list<unit> ) ( s  :  Prims.list<ch> ) -> (

let w = (skip_ws s)
in (match (w) with
| [] -> begin
     PErr (UnexpectedEndOfInput, "unexpected end of input", [])
     end
| (CQuote)::uu___ -> begin
     (match ((parse_string lim.lstr w)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (cs, r) -> begin
     POk (JStr (cs), r)
     end)
     end
| (CLBrace)::uu___ -> begin
     (parse_object float_read cap lim tol b w)
     end
| (CLBrack)::uu___ -> begin
     (parse_array float_read cap lim tol b w)
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
and parse_object : (Prims.list<ch>  ->  freadv)  ->  Prims.string  ->  lims  ->  Prims.bool  ->  Prims.list<unit>  ->  Prims.list<ch>  ->  pres<jval> = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( cap  :  Prims.string ) ( lim  :  lims ) ( tol  :  Prims.bool ) ( b  :  Prims.list<unit> ) ( w  :  Prims.list<ch> ) -> (match (b) with
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
     (match ((parse_members float_read cap lim tol b' [] t1)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (fs, r) -> begin
     POk (JObj (fs), r)
     end)
     end))
     end)
     end))
and parse_array : (Prims.list<ch>  ->  freadv)  ->  Prims.string  ->  lims  ->  Prims.bool  ->  Prims.list<unit>  ->  Prims.list<ch>  ->  pres<jval> = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( cap  :  Prims.string ) ( lim  :  lims ) ( tol  :  Prims.bool ) ( b  :  Prims.list<unit> ) ( w  :  Prims.list<ch> ) -> (match (b) with
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
     (match ((parse_items float_read cap lim tol b' [] t1)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (xs, r) -> begin
     POk (JArr (xs), r)
     end)
     end))
     end)
     end))
and parse_members : (Prims.list<ch>  ->  freadv)  ->  Prims.string  ->  lims  ->  Prims.bool  ->  Prims.list<unit>  ->  Prims.list<(Prims.list<och> * jval)>  ->  Prims.list<ch>  ->  pres<Prims.list<(Prims.list<och> * jval)>> = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( cap  :  Prims.string ) ( lim  :  lims ) ( tol  :  Prims.bool ) ( b  :  Prims.list<unit> ) ( acc  :  Prims.list<(Prims.list<och> * jval)> ) ( s  :  Prims.list<ch> ) -> (

let w = (skip_ws s)
in  
if (spent lim.lmembers acc) then begin
     PErr (LimitExceeded (LMembers), msg_limit, w)
     end else begin
     (match ((parse_string lim.lstr w)) with
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
     (parse_members float_read cap lim tol b acc t6)
     end
| (CRBrace)::t6 -> begin
     POk ((rev acc), t6)
     end
| uu___1 -> begin
     PErr (ExpectedToken, "expected \',\' or \'}\'", t5)
     end))
     end
| FStar_Pervasives_Native.None -> begin
     (match ((parse_value float_read cap lim tol b t3)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (v, t4) -> begin
      
if (has_key key acc) then begin
     PErr (DuplicateMember, msg_duplicate, w)
     end else begin
     (

let t5 = (skip_ws t4)
in (match (t5) with
| (CComma)::t6 -> begin
     (parse_members float_read cap lim tol b ((((key), (v)))::acc) t6)
     end
| (CRBrace)::t6 -> begin
     POk ((rev ((((key), (v)))::acc)), t6)
     end
| uu___1 -> begin
     PErr (ExpectedToken, "expected \',\' or \'}\'", t5)
     end))
     end
     end)
     end)))
     end))
     end)
     end))
and parse_items : (Prims.list<ch>  ->  freadv)  ->  Prims.string  ->  lims  ->  Prims.bool  ->  Prims.list<unit>  ->  Prims.list<jval>  ->  Prims.list<ch>  ->  pres<Prims.list<jval>> = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( cap  :  Prims.string ) ( lim  :  lims ) ( tol  :  Prims.bool ) ( b  :  Prims.list<unit> ) ( acc  :  Prims.list<jval> ) ( s  :  Prims.list<ch> ) ->  
if (spent lim.litems acc) then begin
     PErr (LimitExceeded (LItems), msg_limit, s)
     end else begin
     (match ((parse_value float_read cap lim tol b s)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (v, t) -> begin
     (

let t1 = (skip_ws t)
in (match (t1) with
| (CComma)::t2 -> begin
     (parse_items float_read cap lim tol b ((v)::acc) t2)
     end
| (CRBrack)::t2 -> begin
     POk ((rev ((v)::acc)), t2)
     end
| uu___ -> begin
     PErr (ExpectedToken, "expected \',\' or \']\'", t1)
     end))
     end)
     end)


let parse : (Prims.list<ch>  ->  freadv)  ->  Prims.string  ->  policy  ->  Prims.list<unit>  ->  Prims.list<ch>  ->  jresult = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( cap  :  Prims.string ) ( pol  :  policy ) ( b  :  Prims.list<unit> ) ( s  :  Prims.list<ch> ) -> (

let tol = (match (pol) with
| EraseMemberNull -> begin
     true
     end
| uu___ -> begin
     false
     end)
in (match ((parse_value float_read cap unbounded tol b s)) with
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


let parse_limited : (Prims.list<ch>  ->  freadv)  ->  Prims.string  ->  policy  ->  Prims.list<unit>  ->  lims  ->  Prims.bool  ->  Prims.list<ch>  ->  jresult = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( cap  :  Prims.string ) ( pol  :  policy ) ( b  :  Prims.list<unit> ) ( lim  :  lims ) ( bytes_ok  :  Prims.bool ) ( s  :  Prims.list<ch> ) ->  
if (not (bytes_ok)) then begin
     RLimit (LBytes, s)
     end else begin
     (

let tol = (match (pol) with
| EraseMemberNull -> begin
     true
     end
| uu___ -> begin
     false
     end)
in (match ((parse_value float_read cap lim tol b s)) with
| PErr (MaxDepthExceeded, uu___, a) -> begin
     RLimit (LDepth, a)
     end
| PErr (LimitExceeded (l), uu___, a) -> begin
     RLimit (l, a)
     end
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
     end))
     end)


let dup_doc : Prims.list<ch> = (CLBrace)::(CQuote)::(CLa)::(CQuote)::(CColon)::(CD0)::(CComma)::(CQuote)::(CLa)::(CQuote)::(CColon)::(CD0)::(CRBrace)::[]


let rec fwd_state : Prims.bool  ->  Prims.list<och>  ->  FStar_Pervasives_Native.option<Prims.bool> = (fun ( p  :  Prims.bool ) ( l  :  Prims.list<och> ) -> (match (l) with
| [] -> begin
     FStar_Pervasives_Native.Some (p)
     end
| (o)::t -> begin
     (match ((sur_fault p (och_class o))) with
| FStar_Pervasives_Native.Some (uu___) -> begin
     FStar_Pervasives_Native.None
     end
| FStar_Pervasives_Native.None -> begin
     (fwd_state (Prims.op_Equals (och_class o) SHigh) t)
     end)
     end))


let ochs_well_formed : Prims.list<och>  ->  Prims.bool = (fun ( l  :  Prims.list<och> ) -> (Prims.op_Equals (fwd_state false l) (FStar_Pervasives_Native.Some (false))))


let rec jval_wf : jval  ->  Prims.bool = (fun ( v  :  jval ) -> (match (v) with
| JStr (s) -> begin
     (ochs_well_formed s)
     end
| JArr (xs) -> begin
     (jvals_wf xs)
     end
| JObj (fs) -> begin
     (fields_wf fs)
     end
| uu___ -> begin
     true
     end))
and jvals_wf : Prims.list<jval>  ->  Prims.bool = (fun ( xs  :  Prims.list<jval> ) -> (match (xs) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((jval_wf x) && (jvals_wf t))
     end))
and fields_wf : Prims.list<(Prims.list<och> * jval)>  ->  Prims.bool = (fun ( fs  :  Prims.list<(Prims.list<och> * jval)> ) -> (match (fs) with
| [] -> begin
     true
     end
| ((k, v))::t -> begin
     (((ochs_well_formed k) && (jval_wf v)) && (fields_wf t))
     end))


let rec keys_distinct : Prims.list<(Prims.list<och> * jval)>  ->  Prims.bool = (fun ( fs  :  Prims.list<(Prims.list<och> * jval)> ) -> (match (fs) with
| [] -> begin
     true
     end
| ((k, uu___))::t -> begin
     ((not ((has_key k t))) && (keys_distinct t))
     end))


let rec jval_distinct : jval  ->  Prims.bool = (fun ( v  :  jval ) -> (match (v) with
| JArr (xs) -> begin
     (jvals_distinct xs)
     end
| JObj (fs) -> begin
     ((keys_distinct fs) && (fields_distinct fs))
     end
| uu___ -> begin
     true
     end))
and jvals_distinct : Prims.list<jval>  ->  Prims.bool = (fun ( xs  :  Prims.list<jval> ) -> (match (xs) with
| [] -> begin
     true
     end
| (x)::t -> begin
     ((jval_distinct x) && (jvals_distinct t))
     end))
and fields_distinct : Prims.list<(Prims.list<och> * jval)>  ->  Prims.bool = (fun ( fs  :  Prims.list<(Prims.list<och> * jval)> ) -> (match (fs) with
| [] -> begin
     true
     end
| ((uu___, v))::t -> begin
     ((jval_distinct v) && (fields_distinct t))
     end))


let only_str : Prims.list<unit>  ->  lims = (fun ( m  :  Prims.list<unit> ) -> {lstr = Bounded (m); litems = Unbounded; lmembers = Unbounded})


let only_items : Prims.list<unit>  ->  lims = (fun ( m  :  Prims.list<unit> ) -> {lstr = Unbounded; litems = Bounded (m); lmembers = Unbounded})


let only_members : Prims.list<unit>  ->  lims = (fun ( m  :  Prims.list<unit> ) -> {lstr = Unbounded; litems = Unbounded; lmembers = Bounded (m)})


let bad3 : Prims.list<ch> = (CLBrack)::(CQuote)::(CQuote)::(CComma)::(CQuote)::(CQuote)::(CComma)::(COther ("%"))::(CRBrack)::[]

type tok =
| TNull
| TBool
| TNumber
| TString
| TArray
| TObject


let uu___is_TNull : tok  ->  Prims.bool = (fun ( projectee  :  tok ) -> (match (projectee) with
| TNull -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_TBool : tok  ->  Prims.bool = (fun ( projectee  :  tok ) -> (match (projectee) with
| TBool -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_TNumber : tok  ->  Prims.bool = (fun ( projectee  :  tok ) -> (match (projectee) with
| TNumber -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_TString : tok  ->  Prims.bool = (fun ( projectee  :  tok ) -> (match (projectee) with
| TString -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_TArray : tok  ->  Prims.bool = (fun ( projectee  :  tok ) -> (match (projectee) with
| TArray -> begin
     true
     end
| uu___ -> begin
     false
     end))


let uu___is_TObject : tok  ->  Prims.bool = (fun ( projectee  :  tok ) -> (match (projectee) with
| TObject -> begin
     true
     end
| uu___ -> begin
     false
     end))


let peek_at : Prims.list<ch>  ->  FStar_Pervasives_Native.option<tok> = (fun ( w  :  Prims.list<ch> ) -> (match (w) with
| (CQuote)::uu___ -> begin
     FStar_Pervasives_Native.Some (TString)
     end
| (CLBrace)::uu___ -> begin
     FStar_Pervasives_Native.Some (TObject)
     end
| (CLBrack)::uu___ -> begin
     FStar_Pervasives_Native.Some (TArray)
     end
| (CLt)::uu___ -> begin
     FStar_Pervasives_Native.Some (TBool)
     end
| (CLf)::uu___ -> begin
     FStar_Pervasives_Native.Some (TBool)
     end
| (CLn)::uu___ -> begin
     FStar_Pervasives_Native.Some (TNull)
     end
| (c)::uu___ -> begin
      
if ((Prims.op_Equals c CMinus) || (is_digit c)) then begin
     FStar_Pervasives_Native.Some (TNumber)
     end else begin
     FStar_Pervasives_Native.None
     end
     end
| [] -> begin
     FStar_Pervasives_Native.None
     end))


let peek : Prims.list<ch>  ->  FStar_Pervasives_Native.option<tok> = (fun ( s  :  Prims.list<ch> ) -> (peek_at (skip_ws s)))


let read_null : Prims.list<ch>  ->  pres<unit> = (fun ( s  :  Prims.list<ch> ) -> (

let w = (skip_ws s)
in (match (w) with
| (CLn)::(CLu)::(CLl)::(CLl)::t -> begin
     POk ((), t)
     end
| uu___ -> begin
     PErr (ExpectedToken, (msg_expect_lit "null"), w)
     end)))


let rec rebuild_value : (Prims.list<ch>  ->  freadv)  ->  Prims.string  ->  lims  ->  Prims.list<unit>  ->  Prims.list<ch>  ->  pres<jval> = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( cap  :  Prims.string ) ( lim  :  lims ) ( b  :  Prims.list<unit> ) ( s  :  Prims.list<ch> ) -> (

let w = (skip_ws s)
in (match ((peek_at w)) with
| FStar_Pervasives_Native.Some (TObject) -> begin
     (rebuild_object float_read cap lim b w)
     end
| FStar_Pervasives_Native.Some (TArray) -> begin
     (rebuild_array float_read cap lim b w)
     end
| uu___ -> begin
     (parse_value float_read cap lim false b s)
     end)))
and rebuild_object : (Prims.list<ch>  ->  freadv)  ->  Prims.string  ->  lims  ->  Prims.list<unit>  ->  Prims.list<ch>  ->  pres<jval> = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( cap  :  Prims.string ) ( lim  :  lims ) ( b  :  Prims.list<unit> ) ( w  :  Prims.list<ch> ) -> (match (b) with
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
     (match ((rebuild_members float_read cap lim b' [] t1)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (fs, r) -> begin
     POk (JObj (fs), r)
     end)
     end))
     end)
     end))
and rebuild_array : (Prims.list<ch>  ->  freadv)  ->  Prims.string  ->  lims  ->  Prims.list<unit>  ->  Prims.list<ch>  ->  pres<jval> = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( cap  :  Prims.string ) ( lim  :  lims ) ( b  :  Prims.list<unit> ) ( w  :  Prims.list<ch> ) -> (match (b) with
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
     (match ((rebuild_items float_read cap lim b' [] t1)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (xs, r) -> begin
     POk (JArr (xs), r)
     end)
     end))
     end)
     end))
and rebuild_members : (Prims.list<ch>  ->  freadv)  ->  Prims.string  ->  lims  ->  Prims.list<unit>  ->  Prims.list<(Prims.list<och> * jval)>  ->  Prims.list<ch>  ->  pres<Prims.list<(Prims.list<och> * jval)>> = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( cap  :  Prims.string ) ( lim  :  lims ) ( b  :  Prims.list<unit> ) ( acc  :  Prims.list<(Prims.list<och> * jval)> ) ( s  :  Prims.list<ch> ) -> (

let w = (skip_ws s)
in  
if (spent lim.lmembers acc) then begin
     PErr (LimitExceeded (LMembers), msg_limit, w)
     end else begin
     (match ((parse_string lim.lstr w)) with
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
in (match ((rebuild_value float_read cap lim b t3)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (v, t4) -> begin
      
if (has_key key acc) then begin
     PErr (DuplicateMember, msg_duplicate, w)
     end else begin
     (

let t5 = (skip_ws t4)
in (match (t5) with
| (CComma)::t6 -> begin
     (rebuild_members float_read cap lim b ((((key), (v)))::acc) t6)
     end
| (CRBrace)::t6 -> begin
     POk ((rev ((((key), (v)))::acc)), t6)
     end
| uu___1 -> begin
     PErr (ExpectedToken, "expected \',\' or \'}\'", t5)
     end))
     end
     end))
     end))
     end)
     end))
and rebuild_items : (Prims.list<ch>  ->  freadv)  ->  Prims.string  ->  lims  ->  Prims.list<unit>  ->  Prims.list<jval>  ->  Prims.list<ch>  ->  pres<Prims.list<jval>> = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( cap  :  Prims.string ) ( lim  :  lims ) ( b  :  Prims.list<unit> ) ( acc  :  Prims.list<jval> ) ( s  :  Prims.list<ch> ) ->  
if (spent lim.litems acc) then begin
     PErr (LimitExceeded (LItems), msg_limit, s)
     end else begin
     (match ((rebuild_value float_read cap lim b s)) with
| PErr (k, m, a) -> begin
     PErr (k, m, a)
     end
| POk (v, t) -> begin
     (

let t1 = (skip_ws t)
in (match (t1) with
| (CComma)::t2 -> begin
     (rebuild_items float_read cap lim b ((v)::acc) t2)
     end
| (CRBrack)::t2 -> begin
     POk ((rev ((v)::acc)), t2)
     end
| uu___ -> begin
     PErr (ExpectedToken, "expected \',\' or \']\'", t1)
     end))
     end)
     end)


let read_rebuilt : (Prims.list<ch>  ->  freadv)  ->  Prims.string  ->  Prims.list<unit>  ->  lims  ->  Prims.bool  ->  Prims.list<ch>  ->  jresult = (fun ( float_read  :  Prims.list<ch>  ->  freadv ) ( cap  :  Prims.string ) ( b  :  Prims.list<unit> ) ( lim  :  lims ) ( bytes_ok  :  Prims.bool ) ( s  :  Prims.list<ch> ) ->  
if (not (bytes_ok)) then begin
     RLimit (LBytes, s)
     end else begin
     (match ((rebuild_value float_read cap lim b s)) with
| PErr (MaxDepthExceeded, uu___, a) -> begin
     RLimit (LDepth, a)
     end
| PErr (LimitExceeded (l), uu___, a) -> begin
     RLimit (l, a)
     end
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
     end)
     end)


let nested_doc : Prims.list<ch> = (CLBrace)::(CQuote)::(CLa)::(CQuote)::(CColon)::(CLBrack)::(CD1)::(CComma)::(CLBrace)::(CQuote)::(CLb)::(CQuote)::(CColon)::(CQuote)::(CLc)::(CQuote)::(CRBrace)::(CRBrack)::(CRBrace)::[]


let rebuilt_nested_accepts : Prims.bool = (match ((read_rebuilt (fun ( uu___  :  Prims.list<ch> ) -> FFinite) "c" ((())::(())::(())::[]) unbounded true nested_doc)) with
| ROk (v) -> begin
     true
     end
| uu___ -> begin
     false
     end)


let twins : Prims.list<Twin.twin> = ({Twin.tname = "a-negative-fraction-is-a-number"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (is_json_number ((CMinus)::(CD0)::(CDot)::(CD5)::[])) true))})::({Twin.tname = "a-leading-zero-then-digit-is-not"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (is_json_number ((CD0)::(CD1)::[])) false))})::({Twin.tname = "parse-string-stops-at-the-quote"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (parse_string Unbounded ((CQuote)::(CLa)::(CQuote)::(CSpace)::[])) (POk ((OLit (CLa))::[], (CSpace)::[]))))})::({Twin.tname = "parse-reads-true"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (parse (fun ( uu___1  :  Prims.list<ch> ) -> FFinite) "c" RejectNull [] ((CLt)::(CLr)::(CLu)::(CLe)::[])) (ROk (JBool (true)))))})::({Twin.tname = "parse-refuses-trailing-characters"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (parse (fun ( uu___1  :  Prims.list<ch> ) -> FFinite) "c" RejectNull [] ((CLt)::(CLr)::(CLu)::(CLe)::(CSpace)::(CComma)::[])) (RErr (TrailingCharacters, "trailing characters", (CComma)::[]))))})::({Twin.tname = "parse-refuses-a-repeated-member"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (parse (fun ( uu___1  :  Prims.list<ch> ) -> FFinite) "c" RejectNull ((())::(())::[]) dup_doc) (RErr (DuplicateMember, msg_duplicate, (CQuote)::(CLa)::(CQuote)::(CColon)::(CD0)::(CRBrace)::[]))))})::({Twin.tname = "parse-string-refuses-a-raw-control"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (parse_string Unbounded ((CQuote)::(CTab)::(CQuote)::[])) (PErr (BadEscape, (msg_ctrl CTab), (CTab)::(CQuote)::[]))))})::({Twin.tname = "parse-limited-refuses-the-third-item-before-reading-it"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (parse_limited (fun ( uu___1  :  Prims.list<ch> ) -> FFinite) "c" RejectNull ((())::[]) (only_items ((())::(())::[])) true bad3) (RLimit (LItems, (COther ("%"))::(CRBrack)::[]))))})::({Twin.tname = "parse-limited-at-no-bound-reads-what-parse-reads"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (parse_limited (fun ( uu___1  :  Prims.list<ch> ) -> FFinite) "c" RejectNull [] unbounded true ((CLt)::(CLr)::(CLu)::(CLe)::[])) (ROk (JBool (true)))))})::({Twin.tname = "peek-names-a-null-without-reading-it"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (peek ((CSpace)::(CLn)::(CLu)::(CLl)::(CLl)::[])) (FStar_Pervasives_Native.Some (TNull))))})::({Twin.tname = "read-null-refuses-a-near-miss-as-the-literal"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (read_null ((CLn)::(CLu)::(CLl)::[])) (PErr (ExpectedToken, (msg_expect_lit "null"), (CLn)::(CLu)::(CLl)::[]))))})::({Twin.tname = "the-rebuilt-reading-reads-what-parse-limited-reads"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals (read_rebuilt (fun ( uu___1  :  Prims.list<ch> ) -> FFinite) "c" ((())::(())::(())::[]) unbounded true nested_doc) (parse_limited (fun ( uu___1  :  Prims.list<ch> ) -> FFinite) "c" RejectNull ((())::(())::(())::[]) unbounded true nested_doc)))})::({Twin.tname = "the-rebuilt-reading-accepts-a-nested-document"; Twin.tholds = (fun ( uu___  :  unit ) -> (Prims.op_Equals rebuilt_nested_accepts true))})::[]




