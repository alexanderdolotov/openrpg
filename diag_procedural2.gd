extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_layer = main.get_node("WorldLayer")
	var player = main.get_node("WorldLayer/Player")

	# Spaced at >2s (the real exploration timer's own cadence) so every
	# position actually gets checked, not skipped over.
	for i in range(25):
		player.position = Vector2(2000 + i * 480, -2200 + (i * 733) % 4200)
		await create_timer(2.3).timeout

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
	print("script counts after 25 properly-spaced regions: ", counts)
	print("(boot baseline was: AppleTree=3 GatherableFoliage=5 DecorativeFoliage=4 FishingSpot=5 Foothills=1 RiverCrossing=1)")

	quit()
