extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_reg = main.get_node("/root/World")
	var debug_log = main.get_node("UI/DebugLog")

	for i in range(4):
		var rabbit = world_reg.GetEntity("animal_%d" % i)
		if rabbit:
			rabbit.position = Vector2(5000, 5000)

	var npc = world_reg.GetEntity("npc_0")
	var wolf = world_reg.GetEntity("animal_4")
	wolf.Hunger = 5.0
	wolf.position = npc.position + Vector2(20, 0)
	print("setup done, watching for 15s")

	for i in range(30):
		await create_timer(0.5).timeout

	var full_log = debug_log.get_parsed_text()
	print("=== LOG (last 4000 chars) ===")
	print(full_log.substr(max(0, full_log.length() - 4000)))
	print("=== END LOG ===")
	print("saw animal attack log line? ", full_log.contains("bites") or full_log.contains("mauls") or full_log.contains("misses"))
	print("saw animal chase log line? ", full_log.contains("goes after"))

	quit()
