extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_layer = main.get_node("WorldLayer")
	var counts = {}
	for c in world_layer.get_children():
		var s = c.get_script()
		if s:
			var name = s.resource_path.get_file()
			counts[name] = counts.get(name, 0) + 1
	print("WorldLayer script counts: ", counts)

	# Check WorldRegistry has pine_0/1 and berry_0/1/2 registered.
	var world_reg = main.get_node("/root/World")
	for id in ["pine_0", "pine_1", "berry_0", "berry_1", "berry_2"]:
		var entity = world_reg.GetEntity(id)
		print(id, " registered: ", entity != null, " pos=", (entity.global_position if entity else "n/a"))

	# River length.
	var river = main.get_node("River")
	print("River.Length=", river.Length, " Waves=", river.Waves)

	# River fishing spots exist and are spread across the new length
	# (they're children of Main directly, "always behind" terrain, same
	# as River itself -- not WorldLayer).
	print("Fishing spot count: ", main.get_children().filter(func(c): var s = c.get_script(); return s and s.resource_path.ends_with("FishingSpot.cs")).size())
	for i in range(5):
		var spot = world_reg.GetEntity("fish_%d" % i)
		if spot:
			print("fish_%d at %s" % [i, spot.position])

	quit()
