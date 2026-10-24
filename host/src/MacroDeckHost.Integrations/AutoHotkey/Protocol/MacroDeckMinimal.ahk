; Without the helper a script gets only its parameters, but still reports that it loaded and its runtime
; errors over stdout, so Macro Deck can tell a load error from a quick exit and no error dialog appears.
class MacroDeck {
    static IsAvailable := false
    static Params := Map()

    static _Start(protocolVersion) {
        OnError(ObjBindMethod(this, "_OnError"))
    }

    static _Ready() {
        this._Send('{"t":"ready","v":1}')
    }

    static _OnError(thrown, mode) {
        try {
            if (thrown is Error) {
                line := IsInteger(thrown.Line) ? thrown.Line : 0
                this._Send('{"t":"error","message":' this._Quote(thrown.Message)
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

    static _Quote(value) {
        try
            text := String(value)
        catch
            text := Type(value)
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
}
