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
	var health_bar = main.get_node("UI/VitalsPanel/HealthBar")

	var animal = world_reg.GetEntity("animal_0")
	print("targeting ", animal.name, " health=", animal.Health)

	var landed_a_hit = false
	for attempt in range(6):
		if not is_instance_valid(animal):
			print("animal died -- definitely landed hits")
			landed_a_hit = true
			break
		player.position = animal.position + Vector2(10, 10)
		await process_frame
		await physics_frame
		if not attack_button.visible:
			print("attack button not visible this attempt, skipping")
			continue
		var health_before = animal.Health
		attack_button.emit_signal("pressed")
		await create_timer(4.0).timeout
		if not is_instance_valid(animal):
			print("animal died from this attack")
			landed_a_hit = true
			break
		if animal.Health < health_before:
			print("hit landed: health %s -> %s" % [health_before, animal.Health])
			landed_a_hit = true
			break
		else:
			print("attempt %d missed (health unchanged: %s)" % [attempt, animal.Health])

	print("attack mechanism confirmed working (landed at least one hit across 6 tries): ", landed_a_hit)

	# --- Eating: use the real public ReceiveDamage() (ICombatant) to
	# bring Health down through the actual game mechanism, not a
	# private-field poke.
	print("")
	print("HealthBar value before damage: ", health_bar.value)
	player.ReceiveDamage(40)
	await process_frame
	print("HealthBar value after ReceiveDamage(40): ", health_bar.value)
	print("CanEat (health low, no food): ", player.CanEat())

	var inv_label = main.get_node("UI/VitalsPanel/InventoryLabel")
	var eat_button = panel.get_node("EatButton")
	player.Inventory.Add("apple", 1)
	await process_frame
	await physics_frame
	print("CanEat (health low, has apple): ", player.CanEat())
	print("EatButton visible: ", eat_button.visible)

	var health_before_eat = health_bar.value
	eat_button.emit_signal("pressed")
	await create_timer(4.0).timeout
	print("HealthBar after eating: %s (was %s)" % [health_bar.value, health_before_eat])
	print("inventory after eating: ", inv_label.text)

	quit()
