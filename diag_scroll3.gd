extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	# Let boot's real Log() calls (backend line, 3x NPC spawn, player
	# spawn -- all through the actual C# Main.Log() path, no test
	# shortcuts) run and their deferred scroll flush.
	await process_frame
	await process_frame
	await process_frame

	var log = main.get_node("UI/DebugLog")
	var sb = log.get_v_scroll_bar()

	print("--- after real boot logging ---")
	print("paragraph_count: ", log.get_paragraph_count())
	print("scrollbar: value=%s max=%s page=%s" % [sb.value, sb.max_value, sb.page])
	var at_bottom_1 = sb.value >= sb.max_value - sb.page - 1.0
	print("at true bottom: ", at_bottom_1)

	# Scroll away on purpose (simulating the user reading old messages),
	# then trigger more real Log() calls (via WorldExploration content
	# generation, which logs through Main.Log()) and confirm it does
	# NOT auto-follow while scrolled up -- the other half of the
	# original requirement.
	log.scroll_to_line(0)
	await process_frame
	print("")
	print("--- after manually scrolling to top ---")
	print("scrollbar: value=%s max=%s page=%s" % [sb.value, sb.max_value, sb.page])

	var player = main.get_node("WorldLayer/Player")
	player.position = Vector2(2600, 100)
	await create_timer(3.0).timeout
	print("")
	print("--- after new content-generation log lines fired while scrolled up ---")
	print("paragraph_count: ", log.get_paragraph_count())
	print("scrollbar: value=%s max=%s page=%s" % [sb.value, sb.max_value, sb.page])
	print("correctly STAYED put (did not yank back to bottom): ", sb.value < 50.0)

	# Now scroll back to bottom manually (as if the user caught up
	# reading) and confirm a NEW log line correctly resumes following.
	log.scroll_to_line(log.get_line_count())
	await process_frame
	var pre_count = log.get_paragraph_count()
	player.position = Vector2(-1600, -1800)
	await create_timer(3.0).timeout
	print("")
	print("--- after returning to bottom, then more content generated ---")
	print("paragraph_count: %d -> %d" % [pre_count, log.get_paragraph_count()])
	var at_bottom_2 = sb.value >= sb.max_value - sb.page - 1.0
	print("resumed following once back at bottom: ", at_bottom_2)

	quit()
