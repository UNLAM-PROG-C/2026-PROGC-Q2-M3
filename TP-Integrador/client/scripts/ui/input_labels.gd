extends RefCounted

## Nombres legibles de los controles asignados a las acciones del InputMap, para mostrarlos en
## la interfaz (ayuda, atajos) sin escribir teclas a mano: si cambia el InputMap, cambia el texto.

const MOUSE_BUTTON_NAMES := {
	MOUSE_BUTTON_LEFT: "Clic izquierdo",
	MOUSE_BUTTON_RIGHT: "Clic derecho",
	MOUSE_BUTTON_MIDDLE: "Clic de la rueda",
	MOUSE_BUTTON_WHEEL_UP: "Rueda arriba",
	MOUSE_BUTTON_WHEEL_DOWN: "Rueda abajo",
	MOUSE_BUTTON_WHEEL_LEFT: "Rueda izquierda",
	MOUSE_BUTTON_WHEEL_RIGHT: "Rueda derecha",
}


## Primer control asignado a la acción ("W", "Clic derecho", "Ctrl + Rueda abajo"…), o "" si
## la acción no existe o no tiene teclas ni botones del mouse.
static func for_action(action: String) -> String:
	if not InputMap.has_action(action):
		return ""
	for input_event: InputEvent in InputMap.action_get_events(action):
		var label := for_event(input_event)
		if not label.is_empty():
			return label
	return ""


## Controles de varias acciones unidos con un separador ("W A S D").
static func for_actions(actions: Array, separator := " ") -> String:
	var labels: PackedStringArray = []
	for action: Variant in actions:
		var label := for_action(str(action))
		if not label.is_empty():
			labels.append(label)
	return separator.join(labels)


## Un par de acciones asignadas a la rueda en los dos sentidos con los mismos modificadores
## (acercar/alejar, girar) se muestra como un solo control: "Rueda" o "Ctrl + Rueda".
## Si no son la rueda, son los dos controles separados por " / ".
static func for_wheel_pair(first_action: String, second_action: String) -> String:
	var first := for_action(first_action)
	var second := for_action(second_action)
	for wheel_pair: Array in [["Rueda arriba", "Rueda abajo"], ["Rueda abajo", "Rueda arriba"]]:
		if first.ends_with(wheel_pair[0]) and second.ends_with(wheel_pair[1]):
			var first_prefix := first.trim_suffix(wheel_pair[0])
			if first_prefix == second.trim_suffix(wheel_pair[1]):
				return first_prefix + "Rueda"
	return for_actions([first_action, second_action], " / ")


static func for_event(input_event: InputEvent) -> String:
	var name := ""
	if input_event is InputEventKey:
		var key_event := input_event as InputEventKey
		var keycode := key_event.physical_keycode if key_event.physical_keycode != KEY_NONE else key_event.keycode
		name = OS.get_keycode_string(keycode)
	elif input_event is InputEventMouseButton:
		var button_index := (input_event as InputEventMouseButton).button_index
		name = MOUSE_BUTTON_NAMES.get(button_index, "Botón %d del mouse" % button_index)
	else:
		return ""
	var modifiers := input_event as InputEventWithModifiers
	var prefix := ""
	if modifiers.ctrl_pressed:
		prefix += "Ctrl + "
	if modifiers.shift_pressed:
		prefix += "Shift + "
	if modifiers.alt_pressed:
		prefix += "Alt + "
	return prefix + name
