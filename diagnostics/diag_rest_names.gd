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

	# Name label check.
	var name_label = null
	for c in npc.get_children():
		if c is Label and c.name == "":
			pass
	for c in npc.get_children():
		if c is Label:
			name_label = c
	print("npc name label text: ", (name_label.text if name_label else "NOT FOUND"))

	# Force the wolf tired, away from everything else, and full so it
	# won't hunt instead.
	for i in range(4):
		var r = world_reg.GetEntity("animal_%d" % i)
		if r: r.position = Vector2(5000, 5000)
	wolf.position = Vector2(6000, 6000)
	wolf.Hunger = 100.0
	wolf.Fatigue = 5.0

	print("wolf.Fatigue before: ", wolf.Fatigue, " state: ", wolf.CurrentState)

	for i in range(6):
		await create_timer(0.5).timeout
	print("wolf.Fatigue after 3s: ", wolf.Fatigue, " state (6=Resting expected): ", wolf.CurrentState)

	for i in range(30):
		await create_timer(0.5).timeout
	print("wolf.Fatigue after +15s more: ", wolf.Fatigue, " state (0=Wandering expected once recovered): ", wolf.CurrentState)

	var debug_log = main.get_node("UI/DebugLog")
	var log_text = debug_log.get_parsed_text()
	print("logged 'beds down to rest'? ", log_text.contains("beds down to rest"))

	quit()
