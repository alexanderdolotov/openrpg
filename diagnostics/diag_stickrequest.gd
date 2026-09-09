extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	root.add_child(main)
	await process_frame
	await process_frame

	var player = main.get_node("WorldLayer/Player")
	var finn = main.get_node("WorldLayer/Finn")
	var chat = main.get_node("UI/ChatInput")

	player.global_position = finn.global_position + Vector2(30, 0)
	await process_frame

	chat.text = "Finn, go find a stick"
	chat.emit_signal("text_submitted", chat.text)
	print("asked Finn to find a stick, waiting for a real response (real LLM calls)...")

	# Stay glued to Finn the whole time — his own autonomous wandering
	# would otherwise carry him out of SpeechLog.HearingRadius (260px)
	# well before his next multi-second LLM turn actually checks for it.
	for i in range(120):
		player.global_position = finn.global_position + Vector2(30, 0)
		await process_frame
		await create_timer(0.5).timeout

	print("=== done waiting ===")
	quit()
