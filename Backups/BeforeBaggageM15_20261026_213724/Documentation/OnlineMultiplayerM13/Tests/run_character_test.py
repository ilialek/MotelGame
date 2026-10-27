from pathlib import Path
import subprocess,time,datetime
P=next((q for q in Path(__file__).resolve().parents if (q/'Assets/Game').is_dir()),Path('/Users/ilalekomtsev/MotelGame'));R=P/'Documentation/OnlineMultiplayerM13'/('locomotion-'+datetime.datetime.now().strftime('%Y%m%d-%H%M%S'));R.mkdir();app=P/'Builds/Playtest/MotelGame.app/Contents/MacOS/MotelGame';processes=[]
try:
 for role in ['host','client']:
  cmd=[str(app),'-batchmode','-nographics','-motel-character-test',role,'-motel-character-test-report',str(R/(role+'.txt')),'-motel-character-report',str(R/(role+'-visuals.txt')),'-logFile',str(R/(role+'.log'))]
  p=subprocess.Popen(cmd,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL);processes.append((role,p));print('Started',role,p.pid,flush=True)
  if role=='host':time.sleep(3)
 end=time.monotonic()+120
 while any(p.poll() is None for _,p in processes):
  if any(p.poll() not in [None,0] for _,p in processes):break
  if time.monotonic()>end:raise RuntimeError('Locomotion timeout')
  time.sleep(1)
 for role,p in processes:
  report=(R/(role+'.txt')).read_text() if (R/(role+'.txt')).exists() else 'No report';print(role,p.poll(),report,flush=True)
  visuals=(R/(role+'-visuals.txt')).read_text();print(visuals,flush=True)
  assert visuals.startswith('PASS')
  for check in ['local-player-Idle','local-player-Walk','local-player-Run','remote-player-Idle','remote-player-Walk','remote-player-Run','remote-player-animated-leg']:assert check in visuals,check
 assert all(p.poll()==0 for _,p in processes)
finally:
 for _,p in processes:
  if p.poll() is None:p.terminate()
 print('Results:',R,flush=True)
