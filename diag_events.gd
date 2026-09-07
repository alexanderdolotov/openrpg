extends SceneTree

func _initialize():
	var main = load("res://scenes/Main.tscn").instantiate()
	get_root().add_child(main)
	await process_frame
	await process_frame

	var EventLog = load("res://scripts/WorldEventLog.cs")

	# Maren announces something at (100,100). Finn is nearby (within
	# VisibilityRadius=260), should witness it once.
	EventLog.Announce("Maren", Vector2(100, 100), "Maren picks an apple.")

	var w1 = EventLog.Witnessed("Finn", Vector2(150, 150))
	print("Finn witnesses Maren's action (in range): count=", w1.size())
	if w1.size() > 0:
		print("  entry: ", w1[0])

	var w2 = EventLog.Witnessed("Finn", Vector2(150, 150))
	print("Finn asking again gets nothing new (consumed once): count=", w2.size())

	var w3 = EventLog.Witnessed("Maren", Vector2(100, 100))
	print("Maren never witnesses her own action: count=", w3.size())

	# Someone far away should never witness it at all.
	EventLog.Announce("Wren", Vector2(0, 0), "Wren catches a fish.")
	var w4 = EventLog.Witnessed("Finn", Vector2(5000, 5000))
	print("Someone far away never witnesses it: count=", w4.size())

	# Multiple events between one character's turns should all batch
	# into ONE Witnessed() call, not require separate polls.
	EventLog.Announce("Maren", Vector2(100, 100), "Maren deposits at home.")
	EventLog.Announce("Maren", Vector2(100, 100), "Maren travels to misty_mountains.")
	var w5 = EventLog.Witnessed("Finn", Vector2(150, 150))
	print("multiple events since last check arrive batched together: count=", w5.size(), " (expect 2)")

	quit()
