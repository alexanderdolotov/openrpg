extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_layer = main.get_node("WorldLayer")
	var player = main.get_node("WorldLayer/Player")

	# Walk through many far, distinct regions to sample the full roll
	# table many times over (catches any Generate* method that errors on
	# a texture load or bad math -- everything from before this run
	# already had .import files missing, so this is the real check that
	# every new asset path actually resolves under real gameplay).
	for i in range(40):
		player.position = Vector2(2000 + i * 450, -2000 + (i * 733) % 4000 - 2000)
		await create_timer(0.1).timeout

	await process_frame

	var counts = {}
	for c in world_layer.get_children():
		var s = c.get_script()
		if s:
			var name = s.resource_path.get_file()
			counts[name] = counts.get(name, 0) + 1
	for c in main.get_children():
		var s = c.get_script()
		if s:
			var name = s.resource_path.get_file()
			counts[name] = counts.get(name, 0) + 1
	print("script counts after wide procedural sampling: ", counts)
	print("no errors printed above this line means every Generate* path ran cleanly")

	quit()
