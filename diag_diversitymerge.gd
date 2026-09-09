extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	# Find any NpcAgent to call the (static, wrapped) test hook on.
	var agent = null
	for child in main.get_node("WorldLayer").get_children():
		if child.has_method("__TestMerge"):
			agent = child
			break
	print("found an agent: ", agent != null)

	# The exact motivating scenario: 20 "trees" (all closer, filling
	# category A) vs 1 "stick" (category B, listed after all 20 trees in
	# raw distance order) — the stick MUST still appear, and appear in
	# round 0 (right after tree #0), not buried after all 20 trees.
	var trees = []
	for i in range(20):
		trees.append("tree_%d: 3 apples left, %d px away" % [i, 50 + i])
	var sticks = ["stick_0 (stick): 400 px away, lying on the ground."]

	var merged = agent.__TestMerge(trees, sticks)
	print("total lines returned: ", merged.size())
	print("stick_0 present: ", merged.find("stick_0 (stick): 400 px away, lying on the ground.") != -1)
	print("stick_0 position in result (expect early, e.g. index 1 — right after tree_0): ", merged.find("stick_0 (stick): 400 px away, lying on the ground."))
	print("first 3 lines: ", merged.slice(0, 3))

	# Also confirm the char-budget cutoff actually engages: many long
	# lines should eventually stop getting added once budget runs out,
	# not silently grow forever.
	var long_cat = []
	for i in range(50):
		long_cat.append("padding_%d: this is a deliberately long filler line meant to eat through the shared character budget quickly indeed" % i)
	var merged2 = agent.__TestMerge(long_cat, [])
	var total_chars = 0
	for l in merged2:
		total_chars += l.length()
	print("lines returned from 50 long candidates (expect well under 50): ", merged2.size())
	print("total chars used (expect <= ~1000 budget): ", total_chars)

	quit()
