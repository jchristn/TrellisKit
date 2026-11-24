import { audienceChoices, audiencesWithin, composerAudiences } from '@trelliskit/shared';
import { useState } from 'react';
import { View } from 'react-native';
import { useProperty } from '../property';
import strings from '../strings';
import AppText from './AppText';
import ChipGroup from './ChipGroup';

const s = strings.access;

/**
 * Audience picker state for a composer (ACCESS_MODELS_CONTRACT.md section 8.9): the options the
 * access model allows (Open: SignedIn; PartiallyGated: SignedIn and Members; FullyGated: Members;
 * plus Public for SystemAdmin), defaulting to `access.defaultAudience` (Members in a gated
 * feature). `floor` keeps a thread at least as narrow as its forum; an existing object's audience
 * is kept even when the model no longer offers it.
 * @param {{ feature?: string|null, isSystemAdmin?: boolean, current?: string|null, floor?: string|null }} options
 * @returns {{ options: string[], value: string, setValue: (value: string) => void, visible: boolean }}
 */
export function useAudiencePicker({ feature = null, isSystemAdmin = false, current = null, floor = null } = {}) {
  const property = useProperty();
  const config = composerAudiences(property, { isSystemAdmin, feature });
  const options = audienceChoices(audiencesWithin(config.options, floor), current);
  const [value, setValue] = useState(() => (current && options.includes(current) ? current : options.includes(config.defaultAudience) ? config.defaultAudience : options[0]));
  return { options, value, setValue, visible: options.length > 1 };
}

/**
 * The audience chips with a one-line hint for the chosen audience.
 * @param {{ picker: ReturnType<typeof useAudiencePicker>, testID?: string }} props
 */
export default function AudiencePicker({ picker, testID = 'audience' }) {
  if (!picker.visible) return null;
  return (
    <View style={{ gap: 4 }}>
      <ChipGroup label={s.audienceLabel} options={picker.options.map((a) => ({ value: a, label: s.audiences[a] || a }))} value={picker.value} onToggle={picker.setValue} testID={testID} />
      <AppText variant="caption" color="textMuted" testID={testID + '-hint'}>
        {s.audienceHints[picker.value] || ''}
      </AppText>
    </View>
  );
}
