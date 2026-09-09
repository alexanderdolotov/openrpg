extends SceneTree

func _initialize():
	var main_scene = load("res://scenes/Main.tscn")
	var main = main_scene.instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	print("Main children (in sibling order):")
	for c in main.get_children():
		print("  ", c.name, " : ", c.get_class(), " script=", (c.get_script().resource_path if c.get_script() else "none"))

	var world_layer = main.get_node("WorldLayer")
	print("WorldLayer children:")
	for c in world_layer.get_children():
		print("  ", c.name, " : ", c.get_class(), " script=", (c.get_script().resource_path if c.get_script() else "none"))
		for gc in c.get_children():
			print("    -> ", gc.name, " : ", gc.get_class(), " script=", (gc.get_script().resource_path if gc.get_script() else "none"))

	quit()
