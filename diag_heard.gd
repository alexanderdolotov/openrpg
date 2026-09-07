extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_reg = main.get_node("/root/World")
	var npc = world_reg.GetEntity("npc_0")
	var player = main.get_node("WorldLayer/Player")
	print("npc: ", npc.position, " player: ", player.position)

	# Bring the player within hearing range and speak a direct request.
	player.position = npc.position + Vector2(30, 0)
	# SpeechLog.Say is a static C# call — reach it via the same
	# ChatInput path the player normally uses.
	var chat_input = main.get_node("UI/ChatInput")
	print("chat_input found: ", chat_input != null, " class: ", (chat_input.get_class() if chat_input else "n/a"))
	chat_input.text = "come follow me, I need your help fighting a wolf"
	chat_input.emit_signal("text_submitted", chat_input.text)
	await process_frame
	print("chat_input.text after submit (should be empty if handled): '", chat_input.text, "'")

	var debug_log = main.get_node("UI/DebugLog")
	var start_len = debug_log.get_parsed_text().length()

	for i in range(40):
		await create_timer(0.5).timeout
		var log_text = debug_log.get_parsed_text().substr(start_len)
		if log_text.contains("Maren] thought:") and log_text.contains("Maren] attempting:"):
			break

	var full_log = debug_log.get_parsed_text().substr(start_len)
	print("=== LOG ===")
	print(full_log)
	print("=== END ===")

	quit()
