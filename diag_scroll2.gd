extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var log = main.get_node("UI/DebugLog")
	var sb = log.get_v_scroll_bar()

	for i in range(60):
		log.append_text("[color=#ffffff]stress line %d[/color]\n" % i)
		while log.get_paragraph_count() > 40:
			log.remove_paragraph(0)
	await process_frame
	await process_frame

	print("paragraph_count: ", log.get_paragraph_count())
	print("get_line_count: ", log.get_line_count())
	print("scrollbar before scroll_to_line: value=%s max=%s page=%s" % [sb.value, sb.max_value, sb.page])

	log.scroll_to_line(log.get_line_count())
	await process_frame
	print("scrollbar after scroll_to_line(get_line_count()): value=%s max=%s page=%s" % [sb.value, sb.max_value, sb.page])
	print("looks like the bottom (value close to max-page): ", abs(sb.value - (sb.max_value - sb.page)) < 5.0)

	quit()
