extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	root.add_child(main)
	await process_frame
	await process_frame

	var firepit = main.get_node("WorldLayer/FirePit")
	var grass2 = main.get_node("WorldLayer/Grass2") # the one originally hand-placed at (150,300)
	print("FirePit pos: ", firepit.position)
	print("Grass2 pos (was (150,300), same as FirePit): ", grass2.position)
	print("distance between them: ", firepit.position.distance_to(grass2.position))
	print("no longer overlapping (expect true): ", firepit.position.distance_to(grass2.position) > 20.0)

	var river = main.get_node("River")
	print("River Length (expect 5000): ", river.Length)
	print("River Waves (expect 10): ", river.Waves)
	print("River Segments (expect 92): ", river.Segments)

	var lake = main.get_node_or_null("MountainLake")
	print("MountainLake exists: ", lake != null)
	if lake:
		print("MountainLake Radius (Lake-only property, expect 140): ", lake.Radius)

	var old_deepriver = main.get_node_or_null("DeepRiver")
	print("old DeepRiver node still exists (expect false): ", old_deepriver != null)

	# Sanity-check no OTHER obvious overlaps among named grass/berry nodes.
	var overlaps = []
	var names = []
	for i in range(8):
		names.append("WorldLayer/Grass%d" % i)
	for i in range(6):
		names.append("WorldLayer/Berry%d" % i)
	names.append("WorldLayer/FirePit")
	names.append("WorldLayer/Home")
	for i in range(3):
		names.append("WorldLayer/Tree%d" % i)

	var nodes = []
	for n in names:
		var node = main.get_node_or_null(n)
		if node:
			nodes.append([n, node.position])

	for i in range(nodes.size()):
		for j in range(i+1, nodes.size()):
			var d = nodes[i][1].distance_to(nodes[j][1])
			if d < 15.0:
				overlaps.append("%s <-> %s dist=%s" % [nodes[i][0], nodes[j][0], d])

	print("checked ", nodes.size(), " nodes, overlap pairs (<15px apart): ", overlaps)

	quit()
