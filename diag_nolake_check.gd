extends SceneTree
func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame
	await physics_frame
	print("MountainLake node exists (should be false): ", main.get_node_or_null("MountainLake") != null)
	print("world built without crashing")
	quit()
