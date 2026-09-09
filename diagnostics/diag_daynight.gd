extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var dimmer = main.get_node("WorldLayer/WorldDimmer")
	print("dimmer found: ", dimmer != null, " class: ", (dimmer.get_class() if dimmer else "n/a"))
	print("initial color: ", dimmer.color)

	for i in range(10):
		await create_timer(1.0).timeout
		print("t=", i+1, "s color: ", dimmer.color)

	quit()
