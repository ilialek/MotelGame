from pathlib import Path
import sys

def validate(folder):
 folder=Path(folder)
 for role in ['host','client','observer']:
  assert (folder/(role+'.txt')).read_text().startswith('PASS'),role+' gameplay'
  visual=(folder/(role+'-visuals.txt')).read_text();assert visual.startswith('PASS'),role+' configuration'
  expected=['remote-player-Idle','guest-Idle','four-simultaneous-guests','appearance-Casual','appearance-LongSleeve','appearance-Suit','remote-hand-towel']
  # The late observer joins a stationary occupied-room snapshot and exits before departures.
  # Moving guest animation must be observed by host/client, who stay for the full lifecycle.
  if role!='observer':expected+=['guest-Walk','guest-animated-leg','first-person-towel','guest-phase-Leaving']
  for check in expected:assert check in visual,(role,check)
 return 'PASS gameplay and visual observations for Host, Client and late observer; moving-animation checks apply to the full-lifecycle Host/Client roles.'

if __name__=='__main__':print(validate(sys.argv[1]))
