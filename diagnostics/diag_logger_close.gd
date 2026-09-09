extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var logger = main.get("_thoughtLog")
	print("logger enabled and has a log path: ", logger.LogPath != "")

	logger.Log("test", "TEST", "before close")
	logger.Close()
	# This used to throw ObjectDisposedException, uncaught, before the fix.
	logger.Log("test", "TEST", "after close — should be silently ignored, not crash")
	print("no crash after Log() post-Close() — fix confirmed")

	# Calling Close() again should also be a safe no-op.
	logger.Close()
	print("double-Close() is safe")

	quit()
