import ExpoModulesCore
import Security

/// Offers to save an email and password sign-in to the iCloud Keychain for the app's webcredentials associated
/// domain (SecAddSharedWebCredential). iOS shows its own "Save Password?" prompt, or nothing when the same
/// credential is already saved; the promise resolves true when the credential was saved or already present.
public class SharedCredentialsModule: Module {
  public func definition() -> ModuleDefinition {
    Name("SharedCredentials")

    AsyncFunction("save") { (domain: String, account: String, password: String, promise: Promise) in
      SecAddSharedWebCredential(domain as CFString, account as CFString, password as CFString) { error in
        promise.resolve(error == nil)
      }
    }
  }
}
