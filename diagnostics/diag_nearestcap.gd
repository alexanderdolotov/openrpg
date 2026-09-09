extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_reg = main.get_node("/root/World")
	var wren = world_reg.GetEntity("Wren")

	# Find Wren's NpcAgent sibling (added alongside the actor, not
	# registered by name in WorldRegistry) via duck-typing the test hook.
	var wren_agent = null
	for child in main.get_node("WorldLayer").get_children():
		if child.has_method("__TestBuildPerception") and child.has_method("get") and child.get("Id") == "npc_2":
			wren_agent = child
			break
	print("found Wren's agent: ", wren_agent != null)

	# 10 rabbits stacked right on top of Wren — all well within hearing
	# radius, well past the NearbyResourceCount=6 cap.
	main.__TestSpawnRabbitsAt(wren.global_position, 10)
	await process_frame

	var perception = wren_agent.__TestBuildPerception()
	var animal_lines = 0
	for line in perception.split("\n"):
		if line.find("(rabbit)") != -1 or line.find("(wolf)") != -1 or line.find("(bear)") != -1:
			animal_lines += 1
	print("animal lines in perception (expect capped at 6, NOT 10+): ", animal_lines)

	quit()
