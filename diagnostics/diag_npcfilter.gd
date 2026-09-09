extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	root.add_child(main)
	await process_frame
	await process_frame

	var player = main.get_node("WorldLayer/Player")
	var maren = main.get_node("WorldLayer/Maren")
	var bar = main.get_node("UI/NpcFilterBar")
	var log_label = main.get_node("UI/DebugLog")

	print("player pos: ", player.position, " maren pos: ", maren.position)
	print("initial filter bar children: ", bar.get_child_count())

	# Move player right next to Maren (well within HearingRadius=260) and
	# let RefreshNpcFilterBar's 0.5s timer catch up.
	player.global_position = maren.global_position + Vector2(20, 0)
	for i in range(40):
		await process_frame
		await create_timer(0.05).timeout

	print("after moving near Maren, filter bar children: ", bar.get_child_count())
	for c in bar.get_children():
		print(" - button: ", c.text, " pressed=", c.button_pressed)

	var maren_button = null
	for c in bar.get_children():
		if c.text == "Maren":
			maren_button = c
	print("found Maren button: ", maren_button)

	if maren_button:
		# Simulate a click by emitting the real signal on the real node.
		maren_button.emit_signal("pressed")
		await process_frame
		print("after clicking Maren filter, button pressed=", maren_button.button_pressed)
		print("visible log text length: ", log_label.get_parsed_text().length())
		var text_after_filter = log_label.get_parsed_text()

		# Now walk the player far away and let the timer notice.
		player.global_position = maren.global_position + Vector2(5000, 5000)
		for i in range(40):
			await process_frame
			await create_timer(0.05).timeout

		print("after walking away, filter bar children: ", bar.get_child_count())
		print("active filter auto-cleared? button_pressed on any remaining: ")
		for c in bar.get_children():
			print(" - ", c.text, " pressed=", c.button_pressed)
		var text_after_leaving = log_label.get_parsed_text()
		print("visible text grew after leaving (filter cleared, more shown)? ", text_after_leaving.length() >= text_after_filter.length())
	else:
		print("FAIL: no Maren button appeared")

	quit()
