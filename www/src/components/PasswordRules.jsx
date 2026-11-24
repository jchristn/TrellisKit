import { passwordProblems } from '@trelliskit/shared';
import strings from '../strings.js';
import Icon from './Icon.jsx';

const a = strings.auth;

/** Live password policy hints (10 to 128 characters, not the email). */
function PasswordRules({ id, password, email }) {
  const problems = passwordProblems(password, email);
  const lengthOk = password.length > 0 && !problems.includes('tooShort') && !problems.includes('tooLong');
  const emailOk = password.length > 0 && !problems.includes('matchesEmail');
  const rule = (ok, text) => (
    <li className={ok ? 'rule-ok' : 'rule-pending'}>
      <Icon name={ok ? 'check' : 'info'} size={14} />
      <span>{text}</span>
      <span className="visually-hidden">{ok ? strings.common.yes : strings.common.no}</span>
    </li>
  );
  return (
    <ul id={id} className="password-rules" aria-label={a.passwordRules}>
      {rule(lengthOk, a.ruleLength)}
      {rule(emailOk, a.ruleNotEmail)}
    </ul>
  );
}

export default PasswordRules;
