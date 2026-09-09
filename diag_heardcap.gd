extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var agent = null
	for child in main.get_node("WorldLayer").get_children():
		if child.has_method("__TestCapHeardLines"):
			agent = child
			break
	print("found an agent: ", agent != null)

	# 1 player line + 10 non-player lines, cap of 6 total. Player line
	# MUST survive regardless; only 5 of the 10 non-player lines should
	# make it (the 5 MOST RECENT, i.e. the last 5 in chronological order).
	var player_lines = ["Alex (the real human player, not another character in this world) just said to you: \"let's go fishing\""]
	var non_player = []
	for i in range(10):
		non_player.append("npc_chatter_%d just said to you: \"blah blah %d\"" % [i, i])

	var result = agent.__TestCapHeardLines(player_lines, non_player, 6)
	print("total lines: ", result.size(), " (expect 6)")
	print("player line present: ", Array(result).has(player_lines[0]))
	print("oldest non-player line (chatter_0) dropped: ", not Array(result).has(non_player[0]))
	print("newest non-player line (chatter_9) kept: ", Array(result).has(non_player[9]))
	print("result: ", result)

	quit()
