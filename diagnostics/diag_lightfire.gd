extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	root.add_child(main)
	await process_frame
	await process_frame

	var player = main.get_node("WorldLayer/Player")
	var maren = main.get_node("WorldLayer/Maren")
	var chat = main.get_node("UI/ChatInput")

	# Stand right next to the fire pit alongside Maren so she's in
	# hearing range and can actually reach it.
	var firepit = main.get_node("WorldLayer/FirePit") if main.has_node("WorldLayer/FirePit") else null
	if firepit:
		player.global_position = firepit.global_position + Vector2(20, 0)
		maren.global_position = firepit.global_position + Vector2(-20, 0)
	else:
		player.global_position = maren.global_position + Vector2(20, 0)
	await process_frame

	chat.text = "can someone help me light this fire"
	chat.emit_signal("text_submitted", chat.text)
	print("asked for fire help, waiting...")

	for i in range(200):
		await process_frame
		await create_timer(0.5).timeout

	quit()
