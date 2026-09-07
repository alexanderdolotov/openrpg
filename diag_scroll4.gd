extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)

	var log = main.get_node("UI/DebugLog")
	var sb = log.get_v_scroll_bar()

	for i in range(15):
		await process_frame
		print("frame %d: paragraphs=%d value=%s max=%s page=%s" % [i, log.get_paragraph_count(), sb.value, sb.max_value, sb.page])

	quit()
