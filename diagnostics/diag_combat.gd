extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var player = main.get_node("WorldLayer/Player")
	var world_reg = main.get_node("/root/World")
	var panel = main.get_node("UI/ActionPanel")
	var attack_button = panel.get_node("AttackButton")

	# Find a live animal and walk right up to it.
	var animal = world_reg.GetEntity("animal_0")
	if animal == null:
		print("animal_0 not found, aborting")
		quit()
		return

	print("targeting ", animal.name, " health=", animal.Health)
	player.position = animal.position + Vector2(10, 10)
	await process_frame
	await physics_frame
	print("AttackButton visible: ", attack_button.visible)

	var health_before = animal.Health
	attack_button.emit_signal("pressed")
	await create_timer(4.0).timeout

	if is_instance_valid(animal):
		print("animal health after one attack: ", animal.Health, " (was ", health_before, ")")
	else:
		print("animal died from the attack (was likely low HP or a crit)")

	# --- Eating ---
	var inv_label = main.get_node("UI/VitalsPanel/InventoryLabel")
	var eat_button = panel.get_node("EatButton")
	print("")
	print("Health before manual damage: ", player.Vitals.Health)
	player.Vitals.Damage(40)
	print("Health after manual damage: ", player.Vitals.Health)
	print("CanEat (no food yet): ", player.CanEat())

	# Give the player food the same way a real pick_apple would.
	player.Inventory.Add("apple", 1)
	await process_frame
	await physics_frame
	print("CanEat (has apple, health low): ", player.CanEat())
	print("EatButton visible: ", eat_button.visible)

	var health_before_eat = player.Vitals.Health
	eat_button.emit_signal("pressed")
	await create_timer(4.0).timeout
	print("Health after eating: ", player.Vitals.Health, " (was ", health_before_eat, ")")
	print("inventory after eating (apple should be gone): ", inv_label.text)

	quit()
