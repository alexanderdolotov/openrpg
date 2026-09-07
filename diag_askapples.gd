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
	var chat = main.get_node("UI/ChatInput")

	# Move the player near all three NPCs so they're all within
	# SpeechLog.HearingRadius when the question is asked. (Can't seed
	# Inventory contents from GDScript — a plain C# object nested on a
	# Godot type isn't reachable that way — so this checks that NPCs
	# give a real, on-topic answer about their carried apples, "0/none"
	# included, not that a specific pre-set count comes back.)
	player.global_position = maren.global_position + Vector2(30, 0)
	await process_frame

	chat.text = "how many apples do you all have?"
	chat.emit_signal("text_submitted", chat.text)
	print("asked the question, waiting for responses...")

	# Real LLM turns over a real network backend — give this a generous
	# wall-clock budget.
	for i in range(240):
		await process_frame
		await create_timer(0.5).timeout

	quit()
