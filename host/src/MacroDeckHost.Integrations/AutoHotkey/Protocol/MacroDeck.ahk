; Messages to Macro Deck are stdout lines: a record separator and a JSON object. GetVar answers arrive
; on stdin as one line: request id, tab, ok or error, tab, text with backslash escapes.
class MacroDeck {
    static IsAvailable := true
    static Params := Map()
    static ProtocolVersion := 0
    static _Input := 0
    static _NextId := 0

    static SetVar(name, value) {
        this._Send('{"t":"set","name":' this._Quote(name) ',"value":' this._Quote(value) '}')
    }

    static GetVar(name, default?) {
        previous := A_IsCritical
        Critical("On")
        try {
            id := ++this._NextId
            this._Send('{"t":"get","id":' id ',"name":' this._Quote(name) '}')
            if !this._Input
                this._Input := FileOpen("*", "r", "UTF-8-RAW")
            loop {
                line := this._Input.ReadLine()
                if (line == "" && this._Input.AtEOF)
                    throw Error("Macro Deck did not answer.", -1)
                parts := StrSplit(line, "`t", , 3)
                if (parts.Length < 3 || parts[1] != id)
                    continue
                text := this._Unescape(parts[3])
                if (parts[2] == "ok")
                    return text
                if IsSet(default)
                    return default
                throw Error(text, -1)
            }
        } finally {
            Critical(previous)
        }
    }

    static Notify(title, message := "", severity := "info") {
        if !(severity == "info" || severity == "warning" || severity == "error")
            severity := "info"
        this._Send('{"t":"notify","title":' this._Quote(title) ',"message":' this._Quote(message)
            . ',"level":"' severity '"}')
    }

    static SetResult(text) {
        this._Send('{"t":"result","text":' this._Quote(text) '}')
    }

    static Log(text) {
        this._Send('{"t":"log","text":' this._Quote(text) '}')
    }

    static _Start(protocolVersion) {
        this.ProtocolVersion := protocolVersion
        OnError(ObjBindMethod(this, "_OnError"))
    }

    static _Ready() {
        this._Send('{"t":"ready","v":' this.ProtocolVersion '}')
    }

    static _OnError(thrown, mode) {
        try {
            if (thrown is Error) {
                line := IsInteger(thrown.Line) ? thrown.Line : 0
                this._Send('{"t":"error","message":' this._Quote(thrown.Message)
                    . ',"what":' this._Quote(thrown.What) ',"extra":' this._Quote(thrown.Extra)
                    . ',"file":' this._Quote(thrown.File) ',"line":' line '}')
            } else {
                this._Send('{"t":"error","message":' this._Quote(thrown) ',"line":0}')
            }
        }
        return 1
    }

    static _Send(json) {
        FileAppend(Chr(0x1E) json "`n", "*", "UTF-8-RAW")
    }

    static _Text(value) {
        try
            return String(value)
        catch
            return Type(value)
    }

    static _Quote(value) {
        text := this._Text(value)
        text := StrReplace(text, "\", "\\")
        text := StrReplace(text, '"', '\"')
        text := StrReplace(text, "`r", "\r")
        text := StrReplace(text, "`n", "\n")
        text := StrReplace(text, "`t", "\t")
        position := 1
        while (position := RegExMatch(text, "[\x00-\x1F]", &match, position)) {
            replacement := Format("\u{:04X}", Ord(match[0]))
            text := SubStr(text, 1, position - 1) replacement SubStr(text, position + 1)
            position += StrLen(replacement)
        }
        return '"' text '"'
    }

    static _Unescape(text) {
        result := ""
        position := 1
        while (found := InStr(text, "\", true, position)) {
            result .= SubStr(text, position, found - position)
            next := SubStr(text, found + 1, 1)
            result .= next == "n" ? "`n" : next == "r" ? "`r" : next
            position := found + 2
        }
        return result SubStr(text, position)
    }
}
