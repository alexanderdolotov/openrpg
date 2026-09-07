extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame
	await process_frame

	var world_reg = main.get_node("/root/World")
	var npc = world_reg.GetEntity("npc_0")
	var wolf = world_reg.GetEntity("animal_4")

	# Find the VitalsBarDisplay child under the npc actor and the wolf.
	var npc_bar = null
	for c in npc.get_children():
		if c.get_class() == "Node2D" and c.get_script() != null:
			npc_bar = c
	var wolf_bar = null
	for c in wolf.get_children():
		if c.get_class() == "Node2D" and c.get_script() != null:
			wolf_bar = c

	print("npc_bar found: ", npc_bar != null, " visible=", (npc_bar.visible if npc_bar else "n/a"))
	print("wolf_bar found: ", wolf_bar != null, " visible=", (wolf_bar.visible if wolf_bar else "n/a"))

	var check = main.get_node("UI/SettingsPanel/Margin/VBox/VitalsBarsCheck")
	var panel = main.get_node("UI/SettingsPanel")
	var settings_button = main.get_node("UI/SettingsButton")
	print("panel visible before: ", panel.visible)
	settings_button.emit_signal("pressed")
	await process_frame
	print("panel visible after settings click: ", panel.visible)

	print("checkbox pressed (should match GameSettings.ShowVitalsBars=true default): ", check.button_pressed)
	check.button_pressed = false
	check.emit_signal("toggled", false)
	await process_frame
	await process_frame

	print("npc_bar visible after toggle off: ", npc_bar.visible)
	print("wolf_bar visible after toggle off: ", wolf_bar.visible)

	check.button_pressed = true
	check.emit_signal("toggled", true)
	await process_frame
	await process_frame
	print("npc_bar visible after toggle back on: ", npc_bar.visible)

	var close_button = main.get_node("UI/SettingsPanel/Margin/VBox/CloseButton")
	close_button.emit_signal("pressed")
	await process_frame
	print("panel visible after close: ", panel.visible)

	quit()
