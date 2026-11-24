import { useMemo } from 'react';
import { Navigate, useNavigate } from 'react-router-dom';
import { isFieldRequired, missingProfileFields, onboardingFields, profileFieldsOf } from '@trelliskit/shared';
import strings from '../../strings.js';
import { useAuth } from '../../context/AuthContext.jsx';
import { useProperty } from '../../context/PropertyContext.jsx';
import { useToast } from '../../context/ToastContext.jsx';
import useDocumentMeta from '../../hooks/useDocumentMeta.js';
import { onboardingRedirect } from '../../utils/onboarding.js';
import ProfileForm, { builtInLabel } from '../../components/ProfileForm.jsx';
import { Banner } from '../../components/States.jsx';

const o = strings.onboarding;

/**
 * /app/onboarding/profile: profile setup before the member app opens. New accounts answer the
 * sign-up fields; existing accounts land here when a new required sign-up field was added
 * ("Complete your profile"), with the missing fields named from `Me.profileMissing`.
 */
function ProfileSetupView() {
  const auth = useAuth();
  const toast = useToast();
  const navigate = useNavigate();
  const { property, fill } = useProperty();
  const fields = useMemo(() => profileFieldsOf(property), [property]);
  const me = auth.me;
  const existing = Boolean(me && me.handle && me.displayName);
  const missing = missingProfileFields(me, fields)
    .map((m) => (m.key === 'displayName' || m.key === 'handle' ? builtInLabel(fields, m.key, strings.profileForm[m.key]) : m.label));
  useDocumentMeta(existing ? o.completeMeta : o.profileMeta, strings.member.metaDescription);

  if (me && me.profileComplete) return <Navigate to={onboardingRedirect(me) || '/app'} replace />;

  const required = [builtInLabel(fields, 'displayName', strings.profileForm.displayName), builtInLabel(fields, 'handle', strings.profileForm.handle),
    ...onboardingFields(fields).filter(isFieldRequired).map((f) => f.label)];

  const onSaved = (next) => {
    auth.setMe(next);
    if (!next.profileComplete) return;
    toast.success(strings.profileForm.saved);
    navigate(onboardingRedirect(next) || '/app', { replace: true });
  };

  return (
    <div className="member-view onboarding">
      {!existing && <p className="muted small">{o.stepLabel(2, 3)}</p>}
      <section className="panel">
        <h1>{existing ? o.completeTitle : o.profileTitle}</h1>
        {existing && missing.length > 0
          ? <Banner tone="info" icon="info">{fill(o.completeBody(missing))}</Banner>
          : <p className="muted">{o.profileBodyRequired(required)}</p>}
        <ProfileForm
          me={me}
          mode="onboarding"
          onSaved={onSaved}
          onMeChange={(next, message) => { auth.setMe(next); if (message) toast.success(message); }}
          submitLabel={o.profileSubmit}
          skipLabel={o.profileSkip}
        />
      </section>
    </div>
  );
}

export default ProfileSetupView;
