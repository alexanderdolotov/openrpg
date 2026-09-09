extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_reg = main.get_node("/root/World")
	var wolf = world_reg.GetEntity("animal_4")
	wolf.position = Vector2(6500, 6500)
	wolf.Hunger = 100.0
	wolf.Fatigue = 5.0

	var sleep_label = null
	for c in wolf.get_children():
		if c is Label and c.text == "💤":
			sleep_label = c
	print("sleep_label found: ", sleep_label != null, " visible before: ", (sleep_label.visible if sleep_label else "n/a"))

	for i in range(4):
		await create_timer(0.5).timeout
	print("visible after 4s (Resting expected): ", sleep_label.visible, " state: ", wolf.CurrentState)

	quit()
