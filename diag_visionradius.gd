extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_reg = main.get_node("/root/World")
	var wren = world_reg.GetEntity("Wren")
	var wren_agent = null
	for child in main.get_node("WorldLayer").get_children():
		if child.has_method("__TestBuildPerception") and child.get("Id") == "npc_2":
			wren_agent = child
			break
	print("found Wren's agent: ", wren_agent != null)

	# Move tree_0 to "Mordor" — 3000px away, WAY outside VisionRadius (260).
	var tree0 = world_reg.GetEntity("tree_0")
	tree0.global_position = wren.global_position + Vector2(3000, 0)
	await process_frame

	var perception = wren_agent.__TestBuildPerception()
	print("tree_0 line present in perception text (expect false — too far to 'see'): ", perception.find("tree_0:") != -1)

	# The SEPARATE long-range targeting mechanism should be untouched —
	# tree_0 should still be a valid gather target by id, just not
	# described in the immediate 'what's around you' text.
	var ids = wren_agent.__TestTreeIds()
	print("tree_0 still present in TreeIds() targeting enum (expect true — foreknowledge preserved): ", ids.has("tree_0"))

	quit()
