extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	root.add_child(main)
	await process_frame
	await process_frame

	var player = main.get_node("WorldLayer/Player")
	var maren = main.get_node("WorldLayer/Maren")
	var finn = main.get_node("WorldLayer/Finn")
	var wren = main.get_node("WorldLayer/Wren")
	var firepit = main.get_node("WorldLayer/FirePit")
	var chat = main.get_node("UI/ChatInput")

	# Stand near everyone and near the fire pit.
	player.global_position = firepit.global_position + Vector2(30, 0)
	maren.global_position = firepit.global_position + Vector2(-20, 10)
	finn.global_position = firepit.global_position + Vector2(20, -20)
	wren.global_position = firepit.global_position + Vector2(-10, -20)
	await process_frame

	chat.text = "light the fire and make torches for everyone"
	chat.emit_signal("text_submitted", chat.text)
	print("asked, waiting for responses (real LLM calls, generous budget)...")

	for i in range(300):
		await process_frame
		await create_timer(0.5).timeout

	quit()
