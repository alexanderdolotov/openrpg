extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var player = main.get_node("WorldLayer/Player")
	var sprite = player.get_node("Sprite")
	var frames = sprite.sprite_frames

	print("walk frame count (expect 4): ", frames.get_frame_count("walk"))
	print("idle frame count (expect 1): ", frames.get_frame_count("idle"))

	# Compare each walk frame's region to the idle frame's region to see
	# which column (left=0, center=1, right=2) each one actually is.
	var idle_region = frames.get_frame_texture("idle", 0).region
	print("idle region x (col*16): ", idle_region.position.x, " -> col ", idle_region.position.x / 16)

	for i in range(frames.get_frame_count("walk")):
		var region = frames.get_frame_texture("walk", i).region
		print("walk frame ", i, " region x: ", region.position.x, " -> col ", region.position.x / 16)

	quit()
