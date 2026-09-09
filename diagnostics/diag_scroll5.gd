extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var log = main.get_node("UI/DebugLog")
	var sb = log.get_v_scroll_bar()
	var player = main.get_node("WorldLayer/Player")

	# Push well past MaxLogLines (40) worth of REAL Log() calls by
	# generating exploration content across many distinct far regions
	# in one go -- exercises the exact AppendText+RemoveParagraph
	# compound pattern many times over, through the actual C# Log() path.
	for i in range(30):
		player.position = Vector2(2600 + i * 450, -2000 + i * 130)
		await create_timer(0.15).timeout

	await process_frame
	print("paragraph_count (should be capped at 40): ", log.get_paragraph_count())
	print("scrollbar: value=%s max=%s page=%s" % [sb.value, sb.max_value, sb.page])
	print("ended up at true bottom despite many trims: ", sb.value >= sb.max_value - sb.page - 1.0)

	quit()
