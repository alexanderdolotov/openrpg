extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var agent = null
	for child in main.get_node("WorldLayer").get_children():
		if child.has_method("__TestMergeCandidates"):
			agent = child
			break
	print("found an agent: ", agent != null)

	# The user's exact scenario: 30 nearby trees (50-195px) vs 30 far-away
	# sticks (2000-2145px), each line ~45 chars so a 1000-char budget
	# lands around ~20 total slots — representative of their own "20 env
	# objects" example.
	var tree_lines = []
	var tree_dists = []
	for i in range(30):
		var d = 50.0 + i * 5.0
		tree_lines.append("tree_%d: 3 apples left, %d px away" % [i, d])
		tree_dists.append(d)

	var stick_lines = []
	var stick_dists = []
	for i in range(30):
		var d = 2000.0 + i * 5.0
		stick_lines.append("stick_%d: %d px away, on the ground" % [i, d])
		stick_dists.append(d)

	var merged = agent.__TestMergeCandidates(tree_lines, tree_dists, stick_lines, stick_dists)

	var tree_count = 0
	var stick_count = 0
	for l in merged:
		if l.begins_with("tree_"):
			tree_count += 1
		elif l.begins_with("stick_"):
			stick_count += 1

	print("total lines: ", merged.size())
	print("tree count: ", tree_count, "  stick count: ", stick_count)
	print("at least 1 of each (guaranteed diversity): ", tree_count >= 1 and stick_count >= 1)
	print("trees clearly dominate remaining slots (expect true, they're 40x closer): ", tree_count > stick_count)
	print("first 3 (expect: closest tree, closest stick, then back to trees): ", Array(merged).slice(0, 3))
	print("last line: ", merged[merged.size() - 1])

	quit()
