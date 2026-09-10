
((pyml) ) root@localhost:/data/openrpg/llm_tuning#  python 5_baseline_eval.py --model openrpg-npc_ep4a-llama3.2-3b:latest --val-only
Evaluating 25 examples from ['/data/openrpg/llm_tuning/data/val.jsonl']
model=openrpg-npc_ep4a-llama3.2-3b:latest base_url=http://192.168.1.145:11434 num_ctx=4096 concurrency=4
  10/25...
  20/25...
  25/25...
Done in 12.0s (0.48s/example avg, concurrency=4)

=== Overall: 21/25 correct (84.0%) ===

--- Confusion matrix (rows = expected action, cols = predicted) ---
                       catch_fish       cook_meat             eat    gather_berry gather_pinecone      light_fire      make_torch      pick_apple   pick_up_stick           speak           trade          travel
catch_fish                      2               0               0               0               0               0               0               0               0               0               0               0
cook_meat                       0               2               0               0               0               0               1               0               0               0               0               0
eat                             0               0               1               0               0               0               0               0               0               0               0               0
gather_berry                    0               0               0               3               0               0               0               0               0               0               0               0
gather_pinecone                 0               0               0               0               3               0               0               0               0               0               0               0
light_fire                      0               0               0               0               0               3               0               0               0               0               0               0
pick_apple                      0               0               0               0               0               0               0               1               0               0               0               0
pick_up_stick                   0               0               0               0               0               0               0               0               3               0               0               0
speak                           0               0               0               0               0               0               0               0               0               3               1               2

--- By mode ---
  ambient          2/2 (100.0%)
  player_request   19/23 (82.6%)

--- Per-expected-action recall ---
  catch_fish           2/2 (100.0%)
  cook_meat            2/3 (66.7%)
  eat                  1/1 (100.0%)
  gather_berry         3/3 (100.0%)
  gather_pinecone      3/3 (100.0%)
  light_fire           3/3 (100.0%)
  pick_apple           1/1 (100.0%)
  pick_up_stick        3/3 (100.0%)
  speak                3/6 (50.0%)

--- target_id match rate (given the right action) ---
  12/12 (100.0%)
((pyml) ) root@localhost:/data/openrpg/llm_tuning#


((pyml) ) root@localhost:/data/openrpg/llm_tuning#  python 5_baseline_eval.py --model openrpg-npc_ep5a-llama3.2-3b:latest --val-only
Evaluating 25 examples from ['/data/openrpg/llm_tuning/data/val.jsonl']
model=openrpg-npc_ep5a-llama3.2-3b:latest base_url=http://192.168.1.145:11434 num_ctx=4096 concurrency=4
  10/25...
  20/25...
  25/25...
Done in 12.4s (0.50s/example avg, concurrency=4)

=== Overall: 21/25 correct (84.0%) ===

--- Confusion matrix (rows = expected action, cols = predicted) ---
                       catch_fish       cook_meat             eat    gather_berry gather_pinecone      light_fire      make_torch      pick_apple   pick_up_stick           speak           trade          travel
catch_fish                      2               0               0               0               0               0               0               0               0               0               0               0
cook_meat                       0               2               0               0               0               0               1               0               0               0               0               0
eat                             0               0               1               0               0               0               0               0               0               0               0               0
gather_berry                    0               0               0               3               0               0               0               0               0               0               0               0
gather_pinecone                 0               0               0               0               3               0               0               0               0               0               0               0
light_fire                      0               0               0               0               0               3               0               0               0               0               0               0
pick_apple                      0               0               0               0               0               0               0               1               0               0               0               0
pick_up_stick                   0               0               0               0               0               0               0               0               3               0               0               0
speak                           0               0               0               0               0               0               0               0               0               3               1               2

--- By mode ---
  ambient          2/2 (100.0%)
  player_request   19/23 (82.6%)

--- Per-expected-action recall ---
  catch_fish           2/2 (100.0%)
  cook_meat            2/3 (66.7%)
  eat                  1/1 (100.0%)
  gather_berry         3/3 (100.0%)
  gather_pinecone      3/3 (100.0%)
  light_fire           3/3 (100.0%)
  pick_apple           1/1 (100.0%)
  pick_up_stick        3/3 (100.0%)
  speak                3/6 (50.0%)

--- target_id match rate (given the right action) ---
  11/12 (91.7%)



# Discussion
We can see best tool action finetuning results when training llama for 4 epochs with learning_rate=2e-5
