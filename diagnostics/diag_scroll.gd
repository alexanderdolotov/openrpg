extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var log = main.get_node("UI/DebugLog")
	var sb = log.get_v_scroll_bar()

	# Boot already logged several real lines through the actual C#
	# Log() path (backend line + per-NPC spawn lines + player spawn) --
	# let the deferred scroll-to-bottom flush, then check we're really
	# at the bottom.
	await process_frame
	await process_frame
	print("paragraph count after boot logging: ", log.get_paragraph_count())
	print("scrollbar after boot: value=%s max=%s page=%s" % [sb.value, sb.max_value, sb.page])
	print("at bottom after boot logging: ", sb.value >= sb.max_value - sb.page - 1.0)

	# Directly exercise the exact compound operation Log() performs
	# (AppendText immediately followed by RemoveParagraph in the same
	# call) many times in a tight loop -- the scenario theorized to
	# confuse scroll_follow's own heuristic -- using the real DebugLog
	# node the same way Main.Log() does, to confirm the underlying
	# scrollbar geometry actually settles correctly once given a frame,
	# which is exactly what the deferred call now waits for.
	for i in range(60):
		log.append_text("[color=#ffffff]stress line %d[/color]\n" % i)
		while log.get_paragraph_count() > 40:
			log.remove_paragraph(0)
	await process_frame
	await process_frame
	print("")
	print("paragraph count after 60-line burst (capped at 40): ", log.get_paragraph_count())
	print("scrollbar after burst: value=%s max=%s page=%s" % [sb.value, sb.max_value, sb.page])
	print("scrollbar max_value reflects the trim (not still growing forever): ", sb.max_value < 100000)

	# Confirm setting scrollbar.value = max_value (what ScrollLogToBottom
	# does) actually lands exactly at the true bottom, i.e. this
	# mechanism is sound in principle.
	sb.value = sb.max_value
	await process_frame
	print("after forcing value=max_value, still at max: ", sb.value == sb.max_value)

	quit()
