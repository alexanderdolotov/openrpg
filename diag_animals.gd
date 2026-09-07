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
	print("initial animal counts: ", counts)

	var world_reg = main.get_node("/root/World")
	for id in ["animal_0", "animal_4", "animal_6"]:
		var e = world_reg.GetEntity(id)
		print(id, " registered: ", e != null, " class=", (e.get_class() if e else "n/a"))

	# Let the whole ecosystem actually run for a while -- hunger decay,
	# wandering, wolves potentially hunting rabbits, bears foraging --
	# and confirm nothing crashes across many real physics frames.
	for i in range(20):
		await create_timer(1.0).timeout

	var counts2 = {}
	for c in world_layer.get_children():
		var s = c.get_script()
		if s:
			var name = s.resource_path.get_file()
			counts2[name] = counts2.get(name, 0) + 1
	print("animal counts after 20s of real simulation (no crash = good): ", counts2)

	quit()
