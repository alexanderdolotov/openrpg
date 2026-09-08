extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame
	await physics_frame

	var panel = main.get_node("UI/ActionPanel")
	print("FollowButton removed from scene: ", panel.get_node_or_null("FollowButton") == null)
	print("world built without crashing")
	quit()
