extends SceneTree

func count_by_script(parent, script_suffix):
	var n = 0
	for c in parent.get_children():
		var s = c.get_script()
		if s and s.resource_path.ends_with(script_suffix):
			n += 1
	return n

# Each NPC is wrapped one level down: WorldLayer -> (plain Node, NpcAgent.cs)
# -> CharacterBody2D (NPCActor.cs), named after the NPC. Collect all such
# character bodies plus the player (added directly under WorldLayer).
func find_character_bodies(world_layer):
	var bodies = []
	for c in world_layer.get_children():
		if c.get_class() == "CharacterBody2D":
			bodies.append(c)
		elif c.get_class() == "Node":
			for gc in c.get_children():
				if gc.get_class() == "CharacterBody2D":
					bodies.append(gc)
	return bodies

func _initialize():
	var main_scene = load("res://scenes/Main.tscn")
	var main = main_scene.instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var world_layer = main.get_node("WorldLayer")
	var world_layer_index = world_layer.get_index()

	var before_trees = count_by_script(world_layer, "AppleTree.cs")
	var before_fish = count_by_script(world_layer, "FishingSpot.cs")
	var before_foothills = count_by_script(main, "Foothills.cs")
	var before_rivers = count_by_script(main, "RiverCrossing.cs")
	print("BEFORE: trees=%d fish=%d foothills=%d rivers=%d worldLayerIndex=%d" % [before_trees, before_fish, before_foothills, before_rivers, world_layer_index])

	var bodies = find_character_bodies(world_layer)
	print("found %d character bodies: " % bodies.size(), bodies.map(func(b): return b.name))

	# Distinct, far-apart positions, all well inside
	# WorldExploration.MaxMapBounds (-2000,-2500,5500,5000) but far
	# outside the hand-placed village/frontier — plain Node2D.position
	# writes, no private C# members touched.
	var targets = [Vector2(2600, 100), Vector2(2600, -1800), Vector2(-1600, 1800), Vector2(2600, 1800)]
	for i in range(bodies.size()):
		bodies[i].position = targets[i % targets.size()]
		print("moved ", bodies[i].name, " to ", bodies[i].position)

	# Let the real exploration Timer (WaitTime=2.0, child of WorldLayer)
	# actually fire at least once.
	await create_timer(3.0).timeout

	var after_trees = count_by_script(world_layer, "AppleTree.cs")
	var after_fish = count_by_script(world_layer, "FishingSpot.cs")
	var after_foothills = count_by_script(main, "Foothills.cs")
	var after_rivers = count_by_script(main, "RiverCrossing.cs")
	print("AFTER 1 tick: trees=%d fish=%d foothills=%d rivers=%d" % [after_trees, after_fish, after_foothills, after_rivers])
	print("total new content pieces: ", (after_trees - before_trees) + (after_fish - before_fish) + (after_foothills - before_foothills) + (after_rivers - before_rivers))

	# Confirm generated background pieces (if any) still render BEHIND
	# WorldLayer despite being added long after it already existed.
	var bg_ok = true
	for c in main.get_children():
		var s = c.get_script()
		if s and (s.resource_path.ends_with("Foothills.cs") or s.resource_path.ends_with("RiverCrossing.cs")):
			if c.get_index() > world_layer.get_index():
				bg_ok = false
				print("BAD ordering: ", c.name, " index=", c.get_index(), " worldLayerIndex=", world_layer.get_index())
	print("all background pieces correctly ordered before worldLayer: ", bg_ok)

	# Move everyone back to their exact same far positions again and
	# tick once more — should discover ZERO new regions (already
	# explored), proving dedup actually holds across repeat visits.
	for i in range(bodies.size()):
		bodies[i].position = targets[i % targets.size()]
	await create_timer(3.0).timeout
	var re_trees = count_by_script(world_layer, "AppleTree.cs")
	var re_fish = count_by_script(world_layer, "FishingSpot.cs")
	var re_foothills = count_by_script(main, "Foothills.cs")
	var re_rivers = count_by_script(main, "RiverCrossing.cs")
	print("AFTER repeat-visit tick (should be unchanged): trees=%d fish=%d foothills=%d rivers=%d" % [re_trees, re_fish, re_foothills, re_rivers])
	print("unchanged on repeat visit: ", re_trees == after_trees and re_fish == after_fish and re_foothills == after_foothills and re_rivers == after_rivers)

	quit()
