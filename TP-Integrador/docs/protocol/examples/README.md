# Ejemplos del protocolo

Mensajes de ejemplo para el cliente Godot. Qué permite probar cada uno, y en qué orden usarlos: [`docs/work-plan.md` §2](../../work-plan.md#2-godot-agus).

- **No se editan a mano.** Los genera `server/tests/Rts.Protocol.Tests/Examples/` a partir de los DTOs reales de `Rts.Protocol`, con las mismas opciones JSON que usa el servidor. Si un DTO cambia y estos archivos no se regeneran, `dotnet test` falla.
- **En el cable, cada mensaje es una sola línea** terminada en `\n`. Acá están indentados solo para leerlos mejor.
- **Un campo que no aparece vale `null`**: el servidor omite los nulos. Por ejemplo, `entityResults[].error` falta cuando esa entidad fue aceptada.
- **Todos pertenecen al mismo mundo, visto por P1:** el que crea `server-to-client/02-initial-state.json`. Los `server-to-client` están numerados en orden de partida, así que se pueden aplicar uno tras otro sobre ese mundo.
- **Los números de balance son temporales** (los de `server/config/server.example.json`).

Para regenerarlos después de cambiar un DTO:

```powershell
$env:RTS_UPDATE_PROTOCOL_EXAMPLES=1; dotnet test server/tests/Rts.Protocol.Tests; Remove-Item Env:RTS_UPDATE_PROTOCOL_EXAMPLES
```
