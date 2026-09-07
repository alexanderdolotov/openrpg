extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_reg = main.get_node("/root/World")
	var player = main.get_node("WorldLayer/Player")
	var npc = world_reg.GetEntity("npc_0")
	var firepit = world_reg.GetEntity("firepit")
	var stick = world_reg.GetEntity("stick_0")
	var inv_label = main.get_node("UI/VitalsPanel/InventoryLabel")
	var panel = main.get_node("UI/ActionPanel")

	# Get a stick, light the fire, make a torch.
	player.position = stick.position + Vector2(5, 5)
	await process_frame
	await physics_frame
	panel.get_node("PickUpStickButton").emit_signal("pressed")
	await create_timer(2.5).timeout

	player.position = firepit.position + Vector2(10, 0)
	await process_frame
	await physics_frame
	panel.get_node("LightFireButton").emit_signal("pressed")
	await create_timer(2.5).timeout
	panel.get_node("MakeTorchButton").emit_signal("pressed")
	await create_timer(2.5).timeout
	print("player inventory after crafting torch: ", inv_label.text)

	var player_light = player.get_node("TorchLight")
	print("player TorchLight.visible after crafting: ", player_light.visible)

	# Bring the NPC close and trade the torch away.
	npc.position = player.position + Vector2(30, 0)
	await process_frame
	await physics_frame
	var trade_button = panel.get_node("TradeButton")
	print("trade button visible: ", trade_button.visible, " text: ", trade_button.text)
	trade_button.emit_signal("pressed")
	await create_timer(2.5).timeout

	print("player inventory after trading torch away: ", inv_label.text)
	print("player TorchLight.visible after giving it away (should be false): ", player_light.visible)

	var npc_light = npc.get_node("TorchLight")
	print("npc_0 TorchLight.visible after receiving it: ", npc_light.visible)

	# The trade target is whoever was actually nearest, by name, not
	# necessarily npc_0 — re-check the ACTUAL recipient (registered
	# under their display name too).
	var wren = world_reg.GetEntity("Wren")
	if wren:
		print("Wren TorchLight.visible (the real recipient, should be true): ", wren.get_node("TorchLight").visible)
		print("Wren is same node as npc_0? ", wren == npc)

	quit()
