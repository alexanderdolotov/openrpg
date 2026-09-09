extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	current_scene = main # normally set by Godot's own boot flow (run/main_scene); this diag builds the scene by hand instead
	await process_frame
	await process_frame

	var before = _newest_log()
	print("log file before restart: ", before)

	var restart_button = main.get_node("UI/SettingsPanel/Margin/VBox/RestartButton")
	restart_button.emit_signal("pressed")
	# ReloadCurrentScene() takes effect on the next frame boundary.
	for i in range(5):
		await process_frame

	var main2 = get_root().get_node("Main")
	print("scene actually reloaded (new Main instance): ", main2 != main)

	var after = _newest_log()
	print("log file after restart: ", after)
	print("a new, distinct log file was created (expect true): ", after != before)

	quit()

func _newest_log():
	var dir = DirAccess.open("res://logs")
	var newest = ""
	var newest_time = 0
	for f in dir.get_files():
		if f.ends_with(".log"):
			var t = FileAccess.get_modified_time("res://logs/" + f)
			if t >= newest_time:
				newest_time = t
				newest = f
	return newest
