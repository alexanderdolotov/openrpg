extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_reg = main.get_node("/root/World")
	var player = world_reg.GetEntity("player")
	var wren = world_reg.GetEntity("Wren")

	# Give Wren exactly two distinct items — if the guess were still
	# re-rolling every frame (the old bug), sampling many frames should
	# show BOTH appearing; the fix should show only whichever was rolled
	# first, held stable.
	wren.__TestAddItem("torch", 1)
	wren.__TestAddItem("stick", 2)

	player.global_position = wren.global_position + Vector2(20, 0)
	await process_frame
	await process_frame

	var seen = {}
	var first_item = ""
	for i in range(30):
		await physics_frame
		var item = player.__TestStealGuessItem()
		if item != null:
			seen[item] = true
			if first_item == "":
				first_item = item

	print("distinct items seen across 30 frames: ", seen.keys())
	print("stable (expect true — exactly 1 distinct value, not re-rolling every frame): ", seen.size() == 1)
	print("guessed item is always one Wren actually carries (expect true): ", seen.keys().all(func(k): return k == "torch" or k == "stick"))

	quit()
