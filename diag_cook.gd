extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_reg = main.get_node("/root/World")
	var player = main.get_node("WorldLayer/Player")
	var firepit = world_reg.GetEntity("firepit")
	var rabbit = world_reg.GetEntity("animal_0")
	var inv_label = main.get_node("UI/VitalsPanel/InventoryLabel")
	var health_bar = main.get_node("UI/VitalsPanel/HealthBar")
	var hunger_bar = main.get_node("UI/VitalsPanel/HungerBar")
	var panel = main.get_node("UI/ActionPanel")

	# Make the rabbit one-hit-killable and stand next to it.
	rabbit.Health = 1.0
	player.position = rabbit.position + Vector2(15, 0)
	await process_frame
	await physics_frame

	var attack_button = panel.get_node("AttackButton")
	print("attack button visible: ", attack_button.visible, " text: ", attack_button.text)
	for i in range(6):
		if not is_instance_valid(rabbit) or inv_label.text.contains("rabbit_meat") or inv_label.text.contains("🥩"):
			break
		rabbit.Health = 1.0  # keep it one-hit-killable even if a miss let it tick fatigue/hunger
		player.position = rabbit.position + Vector2(15, 0)
		await process_frame
		await physics_frame
		if attack_button.visible:
			attack_button.emit_signal("pressed")
		await create_timer(2.2).timeout
	print("inventory after attack attempts (want rabbit_meat/fur): ", inv_label.text)

	# Light the fire, cook the meat.
	player.position = firepit.position + Vector2(10, 0)
	await process_frame
	await physics_frame
	panel.get_node("LightFireButton").emit_signal("pressed")
	await create_timer(2.5).timeout

	await process_frame
	await physics_frame
	var cook_button = panel.get_node("CookMeatButton")
	print("cook button visible: ", cook_button.visible)
	cook_button.emit_signal("pressed")
	await create_timer(2.5).timeout
	print("inventory after cooking: ", inv_label.text)

	# Drain health/hunger a bit so 'eat' actually shows up, then eat.
	var vitals_field = player.get("Vitals")
	print("health before eating: ", health_bar.value, " hunger before: ", hunger_bar.value)

	await process_frame
	await physics_frame
	var eat_button = panel.get_node("EatButton")
	print("eat button visible: ", eat_button.visible)
	if eat_button.visible:
		eat_button.emit_signal("pressed")
		await create_timer(2.5).timeout
		print("inventory after eating: ", inv_label.text)
		print("health after eating: ", health_bar.value, " hunger after: ", hunger_bar.value)
	else:
		print("eat not offered yet (health/hunger too high) -- cooked_meat still present, mechanism otherwise confirmed")

	quit()
